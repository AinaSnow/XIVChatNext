using System.Threading.Channels;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

public sealed record HistoryQuery(string? OwnerKey = null, string? Text = null, ushort? Channel = null,
    string? Person = null, DateTime? FromUtc = null, DateTime? UntilUtc = null,
    long BeforeRow = long.MaxValue, int Limit = 100, DateTime? BeforeTimestampUtc = null, string? Source = null,
    string? PeerKey = null, bool BookmarksOnly = false);
public sealed record HistoryRow(long RowId, string Id, string OwnerKey, ServerMessage Message, string Note, bool Bookmarked);

/// <summary>All writes run on one worker. Completion means the transaction committed.</summary>
public sealed partial class HistoryStore : IAsyncDisposable {
    private const int SchemaVersion = 3;
    private readonly string connectionString;
    private readonly SqliteConnection writer;
    private readonly Channel<Write> writes = Channel.CreateBounded<Write>(new BoundedChannelOptions(2048) {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task worker;
    private sealed record Write(Action<SqliteConnection, SqliteTransaction> Run, TaskCompletionSource Completion);

    private HistoryStore(string connectionString, SqliteConnection writer) {
        this.connectionString = connectionString;
        this.writer = writer;
        this.worker = Task.Run(this.RunWriter);
    }

    public static Task<HistoryStore> OpenAsync(string path) => Task.Run(() => {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bool existed = File.Exists(path) && new FileInfo(path).Length > 0;
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(connectionString);
        try {
            connection.Open();
            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"));
            if (version > SchemaVersion) throw new InvalidDataException("History database was created by a newer application.");
            if (version < SchemaVersion) {
                if (existed) {
                    var backupPath = path + $".before-v{SchemaVersion}-{Guid.NewGuid():N}.bak";
                    using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString());
                    backup.Open();
                    connection.BackupDatabase(backup);
                }
                using var tx = connection.BeginTransaction();
                if (version < 1) Execute(connection, Schema, tx);
                if (version < 2) Execute(connection, """
                    CREATE TABLE friend_snapshots (source TEXT NOT NULL, owner_key TEXT NOT NULL,
                        captured_at INTEGER NOT NULL, payload BLOB NOT NULL, PRIMARY KEY(source,owner_key));
                    """, tx);
                if (version < 3) MigrateWorkbench(connection, tx);
                Execute(connection, $"PRAGMA user_version={SchemaVersion}", tx);
                tx.Commit();
            }
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            return new HistoryStore(connectionString, connection);
        } catch {
            connection.Dispose();
            throw;
        }
    });

    private const string Schema = """
        CREATE TABLE messages (
            row_id INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, source TEXT NOT NULL,
            owner_key TEXT NOT NULL, service_id TEXT, run_id TEXT, sequence INTEGER NOT NULL,
            timestamp INTEGER NOT NULL, channel INTEGER NOT NULL, person TEXT NOT NULL,
            search_text TEXT NOT NULL, payload BLOB NOT NULL, note TEXT NOT NULL DEFAULT '',
            bookmarked INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX messages_owner_time ON messages(owner_key,timestamp,row_id);
        CREATE INDEX messages_time ON messages(timestamp,row_id);
        CREATE INDEX messages_stream ON messages(source,service_id,run_id,sequence);
        CREATE VIRTUAL TABLE message_search USING fts5(search_text, note, content='messages', content_rowid='row_id', tokenize='trigram');
        CREATE TRIGGER messages_ai AFTER INSERT ON messages BEGIN
          INSERT INTO message_search(rowid,search_text,note) VALUES(new.row_id,new.search_text,new.note);
        END;
        CREATE TRIGGER messages_ad AFTER DELETE ON messages BEGIN
          INSERT INTO message_search(message_search,rowid,search_text,note) VALUES('delete',old.row_id,old.search_text,old.note);
        END;
        CREATE TRIGGER messages_au AFTER UPDATE ON messages BEGIN
          INSERT INTO message_search(message_search,rowid,search_text,note) VALUES('delete',old.row_id,old.search_text,old.note);
          INSERT INTO message_search(rowid,search_text,note) VALUES(new.row_id,new.search_text,new.note);
        END;
        CREATE TABLE cursors (source TEXT NOT NULL, service_id TEXT NOT NULL, run_id TEXT NOT NULL,
            sequence INTEGER NOT NULL, PRIMARY KEY(source,service_id));
        CREATE TABLE conversation_state (owner_key TEXT NOT NULL, peer_key TEXT NOT NULL, pinned INTEGER NOT NULL DEFAULT 0,
            read_sequence INTEGER NOT NULL DEFAULT 0, draft TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '',
            PRIMARY KEY(owner_key,peer_key));
        CREATE TABLE favorites (id TEXT PRIMARY KEY, kind TEXT NOT NULL, snapshot BLOB NOT NULL,
            group_name TEXT NOT NULL DEFAULT '', tags TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '');
        CREATE TABLE favorite_sources (favorite_id TEXT NOT NULL REFERENCES favorites(id) ON DELETE CASCADE,
            message_id TEXT NOT NULL REFERENCES messages(id), PRIMARY KEY(favorite_id,message_id));
        CREATE TABLE events (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, timestamp INTEGER NOT NULL, kind TEXT NOT NULL, payload BLOB NOT NULL);
        CREATE TABLE avatar_mappings (character_key TEXT PRIMARY KEY, lodestone_id TEXT, manual INTEGER NOT NULL DEFAULT 0,
            image_path TEXT, checked_at INTEGER, retry_at INTEGER);
        CREATE TABLE window_layouts (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
        """;

    // Legacy records deliberately receive local IDs: identical text/timestamps do not prove identity.
    public static string StorageId(string source, ServerMessage message) =>
        $"{source}/{(string.IsNullOrEmpty(message.MessageId) ? "legacy:" + Guid.NewGuid().ToString("N") : message.MessageId)}";

    public Task AppendAsync(string source, ServerMessage message, string id, bool live = false) {
        var payload = MessagePackSerializer.Serialize(message);
        var owner = message.Owner?.Key ?? $"unassigned:{source}";
        var person = message.TellPeer?.Name ?? message.SenderText;
        var search = string.Join("\n", message.ContentText, message.SenderText, person,
            string.Join(" ", message.Chunks.OfType<TextChunk>().Select(chunk => chunk.Content)));
        return this.Enqueue((db, tx) => {
            using var command = Command(db, """
                INSERT OR IGNORE INTO messages(id,source,owner_key,service_id,run_id,sequence,timestamp,channel,person,search_text,payload,peer_key,is_live)
                VALUES($id,$source,$owner,$service,$run,$sequence,$time,$channel,$person,$text,$payload,$peer,$live)
                """, tx, ("$id", id), ("$source", source), ("$owner", owner), ("$service", message.ServiceId),
                ("$run", message.RunId), ("$sequence", message.Sequence), ("$time", Millis(message.Timestamp)),
                ("$channel", (ushort)message.Channel), ("$person", person), ("$text", search), ("$payload", payload),
                ("$peer", ConversationIdentity.IsTell((ushort)message.Channel) ? ConversationIdentity.PeerKey(message.TellPeer) : null), ("$live", live));
            if (command.ExecuteNonQuery() > 0 && message.Owner?.Key != null && ConversationIdentity.IsTell((ushort)message.Channel) &&
                ConversationIdentity.PeerKey(message.TellPeer) is { } peerKey)
                EnsureConversation(db, tx, source, owner, peerKey, message.TellPeer!);
        });
    }

    public Task SaveFriendSnapshotAsync(string source, ServerPlayerList snapshot) {
        if (string.IsNullOrEmpty(source) || snapshot.Type != PlayerListType.Friend || snapshot.Owner?.Key == null ||
            snapshot.Status != FriendListStatus.Success || snapshot.PageIndex != 0 || snapshot.PageCount != 1 ||
            string.IsNullOrEmpty(snapshot.SnapshotId) || !FriendListProtocol.ValidPlayers(snapshot.Players))
            throw new ArgumentException("Only complete, owned friend snapshots can be saved.");
        var owner = snapshot.Owner.Key;
        var captured = Millis(snapshot.CapturedAt);
        var payload = MessagePackSerializer.Serialize(snapshot);
        return this.Enqueue((db, tx) => {
            using var command = Command(db, """
                INSERT INTO friend_snapshots(source,owner_key,captured_at,payload) VALUES($source,$owner,$time,$payload)
                ON CONFLICT(source,owner_key) DO UPDATE SET captured_at=excluded.captured_at,payload=excluded.payload
                WHERE excluded.captured_at >= friend_snapshots.captured_at
                """, tx, ("$source", source), ("$owner", owner), ("$time", captured), ("$payload", payload));
            command.ExecuteNonQuery();
        });
    }

    public Task<ServerPlayerList?> GetFriendSnapshotAsync(string source, string owner) => Task.Run(() => {
        using var db = this.OpenReader();
        using var command = Command(db, "SELECT payload FROM friend_snapshots WHERE source=$source AND owner_key=$owner", null,
            ("$source", source), ("$owner", owner));
        return command.ExecuteScalar() is byte[] payload ? MessagePackSerializer.Deserialize<ServerPlayerList>(payload) : null;
    });

    public Task SaveCursorAsync(string source, HistoryCursor cursor) => this.Enqueue((db, tx) => {
        using var command = Command(db, """
            INSERT INTO cursors(source,service_id,run_id,sequence) VALUES($source,$service,$run,$sequence)
            ON CONFLICT(source,service_id) DO UPDATE SET run_id=excluded.run_id,sequence=excluded.sequence
            """, tx, ("$source", source), ("$service", cursor.ServiceId), ("$run", cursor.RunId), ("$sequence", cursor.Sequence));
        command.ExecuteNonQuery();
    });

    public Task<HistoryCursor?> GetCursorAsync(string source, string service) => Task.Run(() => {
        using var db = this.OpenReader();
        using var cmd = Command(db, "SELECT run_id,sequence FROM cursors WHERE source=$source AND service_id=$service", null,
            ("$source", source), ("$service", service));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new HistoryCursor { ServiceId = service, RunId = reader.GetString(0), Sequence = reader.GetInt64(1) } : null;
    });

    public Task<IReadOnlyList<HistoryRow>> SearchAsync(HistoryQuery query, CancellationToken token = default) => Task.Run<IReadOnlyList<HistoryRow>>(() => {
        token.ThrowIfCancellationRequested();
        using var db = this.OpenReader();
        var conditions = new List<string>();
        using var cmd = db.CreateCommand();
        cmd.Parameters.AddWithValue("$before", query.BeforeRow);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));
        if (query.BeforeTimestampUtc is { } beforeTime) {
            conditions.Add("(m.timestamp < $beforeTime OR (m.timestamp=$beforeTime AND m.row_id < $before))");
            cmd.Parameters.AddWithValue("$beforeTime", Millis(beforeTime));
        } else conditions.Add("m.row_id < $before");
        void Filter(string expression, string key, object? value) {
            if (value == null) return;
            conditions.Add(expression);
            cmd.Parameters.AddWithValue(key, value);
        }
        Filter("m.owner_key=$owner", "$owner", query.OwnerKey);
        Filter("m.source=$source", "$source", query.Source);
        Filter("m.peer_key=$peer", "$peer", query.PeerKey);
        if (query.BookmarksOnly) conditions.Add("m.bookmarked=1");
        Filter("m.channel=$channel", "$channel", query.Channel);
        Filter("instr(m.person,$person)>0", "$person", query.Person);
        Filter("m.timestamp >= $from", "$from", query.FromUtc is { } from ? Millis(from) : null);
        Filter("m.timestamp < $until", "$until", query.UntilUtc is { } until ? Millis(until) : null);
        if (!string.IsNullOrEmpty(query.Text)) {
            // FTS5 trigram cannot find one/two-character queries. Treat all user input literally.
            if (query.Text.EnumerateRunes().Count() >= 3) {
                Filter("m.row_id IN (SELECT rowid FROM message_search WHERE message_search MATCH $text)", "$text",
                    "\"" + query.Text.Replace("\"", "\"\"") + "\"");
            } else Filter("(instr(lower(m.search_text),lower($text))>0 OR instr(lower(m.note),lower($text))>0)", "$text", query.Text);
        }
        cmd.CommandText = "SELECT m.row_id,m.id,m.owner_key,m.payload,m.note,m.bookmarked FROM messages m WHERE "
            + string.Join(" AND ", conditions) + " ORDER BY m.timestamp DESC,m.row_id DESC LIMIT $limit";
        // SqliteCommand.Cancel is a no-op. Interrupt native execution and cover the pre-execution race with a progress callback.
        SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        using var registration = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(db.Handle));
        try {
            token.ThrowIfCancellationRequested();
            using var reader = cmd.ExecuteReader();
            var rows = new List<HistoryRow>();
            while (reader.Read()) {
                token.ThrowIfCancellationRequested();
                rows.Add(new HistoryRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[3]), reader.GetString(4), reader.GetBoolean(5)));
            }
            return rows;
        } catch (SqliteException) when (token.IsCancellationRequested) {
            throw new OperationCanceledException(token);
        } finally {
            SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 0, null, null);
        }
    }, token);

    public Task AnnotateAsync(string id, string note, bool bookmarked) => this.Enqueue((db, tx) => {
        using var command = Command(db, "UPDATE messages SET note=$note,bookmarked=$bookmark WHERE id=$id", tx,
            ("$note", note), ("$bookmark", bookmarked), ("$id", id));
        command.ExecuteNonQuery();
    });

    public Task RetainSourceAsync(string favoriteId, string kind, byte[] snapshot, string messageId) => this.Enqueue((db, tx) => {
        using var command = Command(db, """
            INSERT OR IGNORE INTO favorites(id,kind,snapshot) VALUES($favorite,$kind,$snapshot);
            INSERT OR IGNORE INTO favorite_sources(favorite_id,message_id) VALUES($favorite,$message);
            """, tx, ("$favorite", favoriteId), ("$kind", kind), ("$snapshot", snapshot), ("$message", messageId));
        command.ExecuteNonQuery();
    });

    public Task PruneAsync(int retentionDays, DateTime utcNow) {
        if (retentionDays <= 0) return Task.CompletedTask; // 0 = forever; disabled ingestion is controlled by the application.
        return this.Enqueue((db, tx) => {
            using var command = Command(db, """
                DELETE FROM messages WHERE timestamp < $before AND bookmarked=0 AND note=''
                AND NOT EXISTS (SELECT 1 FROM favorite_sources WHERE message_id=messages.id)
                """, tx, ("$before", Millis(utcNow.AddDays(-retentionDays))));
            command.ExecuteNonQuery();
        });
    }

    public Task FlushAsync() => this.Enqueue((_, _) => { });
    private async Task Enqueue(Action<SqliteConnection, SqliteTransaction> action) {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await this.writes.Writer.WriteAsync(new Write(action, completion));
        await completion.Task;
    }

    private async Task RunWriter() {
        await foreach (var first in this.writes.Reader.ReadAllAsync()) {
            var batch = new List<Write> { first };
            while (batch.Count < 100 && this.writes.Reader.TryRead(out var next)) batch.Add(next);
            try {
                using var tx = this.writer.BeginTransaction();
                foreach (var write in batch) write.Run(this.writer, tx);
                tx.Commit();
                foreach (var write in batch) write.Completion.TrySetResult();
            } catch (Exception ex) {
                foreach (var write in batch) write.Completion.TrySetException(ex);
            }
        }
    }

    private SqliteConnection OpenReader() {
        var db = new SqliteConnection(this.connectionString);
        db.Open();
        return db;
    }
    private static long Millis(DateTime value) => new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();
    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string Key, object? Value)[] parameters) {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null) {
        using var cmd = Command(db, sql, tx);
        cmd.ExecuteNonQuery();
    }
    private static object? Scalar(SqliteConnection db, string sql) {
        using var cmd = Command(db, sql);
        return cmd.ExecuteScalar();
    }
    public async ValueTask DisposeAsync() {
        this.writes.Writer.TryComplete();
        await this.worker;
        await this.writer.DisposeAsync();
    }
}
