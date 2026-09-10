using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

public sealed record ConversationState(string Source, string OwnerKey, string PeerKey, CharacterIdentity Peer,
    bool Pinned = false, string Draft = "", string Note = "", long ReadTime = 0, long ReadRow = 0);
public sealed record ConversationSnapshot(ConversationState State, HistoryRow? Latest, int Unread);
public sealed record HistoryOwner(string Source, string OwnerKey, CharacterIdentity? Identity);
public sealed record AvatarMapping(string CharacterKey, string? LodestoneId, bool Manual, string? ImagePath,
    DateTime? CheckedAt, DateTime? RetryAt, bool Disabled = false);

public sealed partial class HistoryStore {
    public Task<IReadOnlyList<HistoryRow>> GetContextAsync(string id, int radius = 20, CancellationToken token = default) => Task.Run<IReadOnlyList<HistoryRow>>(() => {
        token.ThrowIfCancellationRequested();
        using var db = OpenReader();
        using var tx = db.BeginTransaction();
        using var anchor = Command(db, "SELECT source,owner_key,timestamp,row_id FROM messages WHERE id=$id", tx, ("$id", id));
        string source, owner; long time, row;
        using (var r = anchor.ExecuteReader()) {
            if (!r.Read()) return Array.Empty<HistoryRow>();
            source = r.GetString(0); owner = r.GetString(1); time = r.GetInt64(2); row = r.GetInt64(3);
        }
        var result = new List<HistoryRow>();
        foreach (bool before in new[] { true, false }) {
            var condition = before ? "(timestamp<$time OR (timestamp=$time AND row_id<=$row))" : "(timestamp>$time OR (timestamp=$time AND row_id>$row))";
            var order = before ? "DESC" : "ASC";
            using var cmd = Command(db, $"SELECT row_id,id,owner_key,payload,note,bookmarked FROM messages WHERE source=$source AND owner_key=$owner AND {condition} ORDER BY timestamp {order},row_id {order} LIMIT $limit", tx,
                ("$source", source), ("$owner", owner), ("$time", time), ("$row", row), ("$limit", Math.Clamp(radius, 1, 100) + (before ? 1 : 0)));
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                token.ThrowIfCancellationRequested();
                result.Add(new HistoryRow(r.GetInt64(0), r.GetString(1), r.GetString(2), MessagePackSerializer.Deserialize<ServerMessage>((byte[])r[3]), r.GetString(4), r.GetBoolean(5)));
            }
        }
        return result.OrderBy(r => r.Message.Timestamp).ThenBy(r => r.RowId).ToArray();
    }, token);

    private static void MigrateWorkbench(SqliteConnection db, SqliteTransaction tx) {
        Execute(db, """
            ALTER TABLE messages ADD COLUMN peer_key TEXT;
            ALTER TABLE messages ADD COLUMN is_live INTEGER NOT NULL DEFAULT 0;
            CREATE INDEX messages_conversation ON messages(source,owner_key,peer_key,timestamp,row_id);
            CREATE TABLE conversations (source TEXT NOT NULL, owner_key TEXT NOT NULL, peer_key TEXT NOT NULL,
                peer BLOB NOT NULL, pinned INTEGER NOT NULL DEFAULT 0, draft TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '',
                read_time INTEGER NOT NULL DEFAULT 0, read_row INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(source,owner_key,peer_key));
            ALTER TABLE avatar_mappings ADD COLUMN disabled INTEGER NOT NULL DEFAULT 0;
            """, tx);
        // Backfill only explicit TellPeer identities. Display text and incomplete names never establish identity.
        var old = new List<(long Row, string Source, string Owner, CharacterIdentity Peer)>();
        using (var cmd = Command(db, "SELECT row_id,source,owner_key,payload FROM messages WHERE (channel & 127) IN (12,13)", tx))
        using (var reader = cmd.ExecuteReader()) {
            while (reader.Read()) {
                var message = MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[3]);
                if (message.Owner?.Key != null && ConversationIdentity.PeerKey(message.TellPeer) != null)
                    old.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), message.TellPeer!));
            }
        }
        foreach (var item in old) {
            var key = ConversationIdentity.PeerKey(item.Peer)!;
            using var cmd = Command(db, "UPDATE messages SET peer_key=$peer WHERE row_id=$row", tx, ("$peer", key), ("$row", item.Row));
            cmd.ExecuteNonQuery();
            EnsureConversation(db, tx, item.Source, item.Owner, key, item.Peer);
        }
    }

    private static void EnsureConversation(SqliteConnection db, SqliteTransaction tx, string source, string owner, string key, CharacterIdentity peer) {
        using var cmd = Command(db, """
            INSERT OR IGNORE INTO conversations(source,owner_key,peer_key,peer) VALUES($source,$owner,$key,$peer)
            """, tx, ("$source", source), ("$owner", owner), ("$key", key), ("$peer", MessagePackSerializer.Serialize(peer)));
        cmd.ExecuteNonQuery();
    }

    public Task SaveConversationAsync(ConversationState state) {
        if (ConversationIdentity.PeerKey(state.Peer) != state.PeerKey || string.IsNullOrEmpty(state.OwnerKey) ||
            state.Draft.Length > 8192 || state.Note.Length > 8192) throw new ArgumentException("Invalid conversation state.");
        var peer = MessagePackSerializer.Serialize(state.Peer);
        return Enqueue((db, tx) => {
            using var cmd = Command(db, """
                INSERT INTO conversations(source,owner_key,peer_key,peer,pinned,draft,note) VALUES($source,$owner,$key,$peer,$pin,$draft,$note)
                ON CONFLICT(source,owner_key,peer_key) DO UPDATE SET peer=excluded.peer,pinned=excluded.pinned,draft=excluded.draft,note=excluded.note
                """, tx, ("$source", state.Source), ("$owner", state.OwnerKey), ("$key", state.PeerKey), ("$peer", peer),
                ("$pin", state.Pinned), ("$draft", state.Draft), ("$note", state.Note));
            cmd.ExecuteNonQuery();
        });
    }

    public Task MarkConversationReadAsync(string source, string owner, string peer, DateTime timestamp, long row) => Enqueue((db, tx) => {
        using var cmd = Command(db, """
            UPDATE conversations SET read_time=$time,read_row=$row WHERE source=$source AND owner_key=$owner AND peer_key=$peer
            AND (read_time<$time OR (read_time=$time AND read_row<$row))
            """, tx, ("$source", source), ("$owner", owner), ("$peer", peer), ("$time", Millis(timestamp)), ("$row", row));
        cmd.ExecuteNonQuery();
    });

    public Task<IReadOnlyList<ConversationSnapshot>> GetConversationsAsync(string source, string owner) => Task.Run<IReadOnlyList<ConversationSnapshot>>(() => {
        using var db = OpenReader();
        using var tx = db.BeginTransaction();
        using var cmd = Command(db, """
            SELECT c.peer_key,c.peer,c.pinned,c.draft,c.note,c.read_time,c.read_row,
              m.row_id,m.id,m.payload,m.note,m.bookmarked,
              (SELECT COUNT(*) FROM messages u WHERE u.source=c.source AND u.owner_key=c.owner_key AND u.peer_key=c.peer_key
                 AND (u.channel & 127)=13 AND u.is_live=1 AND (u.timestamp>c.read_time OR (u.timestamp=c.read_time AND u.row_id>c.read_row)))
            FROM conversations c LEFT JOIN messages m ON m.row_id=(SELECT row_id FROM messages l
              WHERE l.source=c.source AND l.owner_key=c.owner_key AND l.peer_key=c.peer_key ORDER BY timestamp DESC,row_id DESC LIMIT 1)
            WHERE c.source=$source AND c.owner_key=$owner ORDER BY c.pinned DESC,m.timestamp DESC,c.peer_key
            """, tx, ("$source", source), ("$owner", owner));
        using var reader = cmd.ExecuteReader();
        var result = new List<ConversationSnapshot>();
        while (reader.Read()) {
            var state = new ConversationState(source, owner, reader.GetString(0), MessagePackSerializer.Deserialize<CharacterIdentity>((byte[])reader[1]),
                reader.GetBoolean(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6));
            HistoryRow? latest = reader.IsDBNull(7) ? null : new HistoryRow(reader.GetInt64(7), reader.GetString(8), owner,
                MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[9]), reader.GetString(10), reader.GetBoolean(11));
            result.Add(new ConversationSnapshot(state, latest, reader.GetInt32(12)));
        }
        return result;
    });

    public Task<IReadOnlyList<HistoryOwner>> GetOwnersAsync() => Task.Run<IReadOnlyList<HistoryOwner>>(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT source,owner_key,payload FROM messages WHERE row_id IN (SELECT MAX(row_id) FROM messages GROUP BY source,owner_key) ORDER BY timestamp DESC");
        using var reader = cmd.ExecuteReader();
        var owners = new List<HistoryOwner>();
        while (reader.Read()) owners.Add(new HistoryOwner(reader.GetString(0), reader.GetString(1),
            MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[2]).Owner));
        return owners;
    });

    public Task<HistoryRow?> GetMessageAsync(string id) => Task.Run(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT row_id,id,owner_key,payload,note,bookmarked FROM messages WHERE id=$id", null, ("$id", id));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new HistoryRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[3]), reader.GetString(4), reader.GetBoolean(5)) : null;
    });

    public Task<AvatarMapping?> GetAvatarAsync(string key) => Task.Run(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT lodestone_id,manual,image_path,checked_at,retry_at,disabled FROM avatar_mappings WHERE character_key=$key", null, ("$key", key));
        using var r = cmd.ExecuteReader();
        DateTime? Time(int i) => r.IsDBNull(i) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(i)).UtcDateTime;
        return r.Read() ? new AvatarMapping(key, r.IsDBNull(0) ? null : r.GetString(0), r.GetBoolean(1), r.IsDBNull(2) ? null : r.GetString(2), Time(3), Time(4), r.GetBoolean(5)) : null;
    });

    public Task SaveAvatarAsync(AvatarMapping avatar) => Enqueue((db, tx) => {
        using var cmd = Command(db, """
            INSERT INTO avatar_mappings(character_key,lodestone_id,manual,image_path,checked_at,retry_at,disabled) VALUES($key,$id,$manual,$path,$checked,$retry,$disabled)
            ON CONFLICT(character_key) DO UPDATE SET lodestone_id=excluded.lodestone_id,manual=excluded.manual,image_path=excluded.image_path,
                checked_at=excluded.checked_at,retry_at=excluded.retry_at,disabled=excluded.disabled
            """, tx, ("$key", avatar.CharacterKey), ("$id", avatar.LodestoneId), ("$manual", avatar.Manual), ("$path", avatar.ImagePath),
            ("$checked", avatar.CheckedAt is { } checkedAt ? Millis(checkedAt) : null), ("$retry", avatar.RetryAt is { } retry ? Millis(retry) : null), ("$disabled", avatar.Disabled));
        cmd.ExecuteNonQuery();
    });
}
