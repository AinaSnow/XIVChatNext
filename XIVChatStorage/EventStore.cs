using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

public sealed record EventQuery(string? Source = null, string? OwnerKey = null, GameEventKind? Kind = null,
    DateTime? BeforeTimestampUtc = null, string? BeforeId = null, int Limit = 100);
public sealed record EventRow(string Id, string Source, string OwnerKey, ServerGameEvent Event, bool Read);

public sealed partial class HistoryStore {
    private static void MigrateEvents(SqliteConnection db, SqliteTransaction tx) => Execute(db, """
        ALTER TABLE events ADD COLUMN source TEXT NOT NULL DEFAULT '';
        ALTER TABLE events ADD COLUMN is_read INTEGER NOT NULL DEFAULT 1;
        CREATE INDEX events_source_owner_time ON events(source,owner_key,timestamp,id);
        CREATE INDEX events_time ON events(timestamp,id);
        """, tx);

    public static string EventStorageId(string source, ServerGameEvent entry) => source + "/" + entry.EventId;

    public async Task<bool> AppendEventAsync(string source, ServerGameEvent entry, bool read = false) {
        if (string.IsNullOrWhiteSpace(source) || !entry.IsValid()) throw new ArgumentException("Invalid event.");
        var payload = MessagePackSerializer.Serialize(entry);
        if (payload.Length > ServerGameEvent.MaxPacketBytes) throw new ArgumentException("Event is too large.");
        var inserted = false;
        await Enqueue((db, tx) => {
            using var cmd = Command(db, """
                INSERT OR IGNORE INTO events(id,source,owner_key,timestamp,kind,payload,is_read)
                VALUES($id,$source,$owner,$time,$kind,$payload,$read)
                """, tx, ("$id", EventStorageId(source, entry)), ("$source", source),
                ("$owner", entry.Owner?.Key ?? "unassigned:" + source), ("$time", Millis(entry.Timestamp)),
                ("$kind", entry.Kind.ToString()), ("$payload", payload), ("$read", read));
            inserted = cmd.ExecuteNonQuery() > 0;
        });
        return inserted;
    }

    public Task<IReadOnlyList<EventRow>> GetEventsAsync(EventQuery query, CancellationToken token = default) => Task.Run<IReadOnlyList<EventRow>>(() => {
        using var db = OpenReader();
        using var cmd = db.CreateCommand();
        var where = new List<string> { "source<>''" };
        void Filter(string sql, string key, object? value) {
            if (value == null) return;
            where.Add(sql); cmd.Parameters.AddWithValue(key, value);
        }
        Filter("source=$source", "$source", query.Source);
        Filter("owner_key=$owner", "$owner", query.OwnerKey);
        Filter("kind=$kind", "$kind", query.Kind?.ToString());
        if (query.BeforeTimestampUtc is { } before) {
            where.Add("(timestamp<$before OR (timestamp=$before AND id<$id))");
            cmd.Parameters.AddWithValue("$before", Millis(before)); cmd.Parameters.AddWithValue("$id", query.BeforeId ?? "");
        }
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 200));
        cmd.CommandText = "SELECT id,source,owner_key,payload,is_read FROM events WHERE " + string.Join(" AND ", where) + " ORDER BY timestamp DESC,id DESC LIMIT $limit";
        var rows = new List<EventRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { token.ThrowIfCancellationRequested(); rows.Add(ReadEvent(reader)); }
        return rows;
    }, token);

    public Task<EventRow?> GetEventAsync(string id) => Task.Run(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT id,source,owner_key,payload,is_read FROM events WHERE id=$id AND source<>''", null, ("$id", id));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEvent(reader) : null;
    });

    public Task<IReadOnlyList<HistoryOwner>> GetEventOwnersAsync() => Task.Run<IReadOnlyList<HistoryOwner>>(() => {
        using var db = OpenReader();
        using var cmd = Command(db, """
            SELECT e.source,e.owner_key,e.payload FROM events e WHERE e.source<>'' AND e.rowid=(
              SELECT newest.rowid FROM events newest WHERE newest.source=e.source AND newest.owner_key=e.owner_key
              ORDER BY newest.timestamp DESC,newest.id DESC LIMIT 1)
            ORDER BY e.timestamp DESC LIMIT 200
            """);
        var result = new List<HistoryOwner>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(new HistoryOwner(reader.GetString(0), reader.GetString(1),
            MessagePackSerializer.Deserialize<ServerGameEvent>((byte[])reader[2]).Owner));
        return result;
    });

    public Task MarkEventsReadAsync(IEnumerable<string> ids) {
        var selected = ids.Distinct().Take(200).ToArray();
        return Enqueue((db, tx) => {
            foreach (var id in selected) {
                using var cmd = Command(db, "UPDATE events SET is_read=1 WHERE id=$id", tx, ("$id", id));
                cmd.ExecuteNonQuery();
            }
        });
    }

    private static EventRow ReadEvent(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
        MessagePackSerializer.Deserialize<ServerGameEvent>((byte[])reader[3]), reader.GetBoolean(4));

    public Task<int> GetUnreadEventCountAsync(string? source, string? owner) => Task.Run(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT COUNT(*) FROM events WHERE source<>'' AND is_read=0 AND ($source IS NULL OR source=$source) AND ($owner IS NULL OR owner_key=$owner)",
            null, ("$source", source), ("$owner", owner));
        return (int)Math.Min(int.MaxValue, Convert.ToInt64(cmd.ExecuteScalar()));
    });
}
