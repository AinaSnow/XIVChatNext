using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

[MessagePackObject]
public sealed class CardFavorite {
    [Key(0)] public string Id { get; set; } = "";
    [Key(1)] public string Source { get; set; } = "";
    [Key(2)] public string OwnerKey { get; set; } = "";
    [Key(3)] public string Name { get; set; } = "";
    [Key(4)] public TextChunk Snapshot { get; set; } = new("");
    [Key(5)] public long SavedAt { get; set; }
    [Key(6)] public bool IsMap { get; set; }
    [Key(7)] public string Group { get; set; } = "";
    [Key(8)] public string Tags { get; set; } = "";
    [Key(9)] public string Note { get; set; } = "";
}

public sealed partial class HistoryStore {
    private static void MigrateCards(SqliteConnection db, SqliteTransaction tx) => Execute(db, """
        CREATE TABLE card_cache (source TEXT NOT NULL,version TEXT NOT NULL,language TEXT NOT NULL,data_scope TEXT NOT NULL,
            query INTEGER NOT NULL,item_id INTEGER NOT NULL,kind INTEGER NOT NULL,page INTEGER NOT NULL,captured_at INTEGER NOT NULL,payload BLOB NOT NULL,
            PRIMARY KEY(source,version,language,data_scope,query,item_id,kind,page));
        CREATE INDEX card_cache_latest ON card_cache(source,query,item_id,kind,page,captured_at DESC);
        CREATE TABLE card_equipment (source TEXT NOT NULL,owner_key TEXT NOT NULL,captured_at INTEGER NOT NULL,payload BLOB NOT NULL,PRIMARY KEY(source,owner_key));
        CREATE TABLE card_favorites (id TEXT PRIMARY KEY REFERENCES favorites(id) ON DELETE CASCADE,source TEXT NOT NULL,owner_key TEXT NOT NULL,
            name TEXT NOT NULL,saved_at INTEGER NOT NULL);
        CREATE INDEX card_favorites_owner ON card_favorites(source,owner_key,saved_at DESC,id);
        """, tx);

    public Task SaveCardPageAsync(string source, ServerGameCard page) {
        if (source.Length is 0 or > 256 || !page.Valid || page.Status != CardStatus.Success || page.Query == CardQuery.Equipment || page.Source == null)
            throw new ArgumentException("Only validated static game card pages may be cached.");
        var payload = MessagePackSerializer.Serialize(page);
        if (payload.Length > CardProtocol.MaxPacketBytes) throw new ArgumentException("Card page exceeds its budget.");
        return this.Enqueue((db, tx) => {
            using var cmd = Command(db, """
                INSERT INTO card_cache(source,version,language,data_scope,query,item_id,kind,page,captured_at,payload)
                VALUES($source,$version,$language,$scope,$query,$id,$kind,$page,$time,$payload)
                ON CONFLICT(source,version,language,data_scope,query,item_id,kind,page) DO UPDATE SET captured_at=excluded.captured_at,payload=excluded.payload;
                DELETE FROM card_cache WHERE rowid NOT IN (SELECT rowid FROM card_cache ORDER BY captured_at DESC,rowid DESC LIMIT 1024);
                """, tx, ("$source", source), ("$version", page.Source.Version ?? ""), ("$language", page.Source.Language),
                ("$scope", page.DataScope), ("$query", (int)page.Query), ("$id", page.Id), ("$kind", page.ItemKind), ("$page", page.Page),
                ("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$payload", payload));
            cmd.ExecuteNonQuery();
        });
    }
    public Task<ServerGameCard?> GetCardPageAsync(string source, CardQuery query, uint id, uint kind, int page = 0,
        string? version = null, string? language = null, string? dataScope = null) => Task.Run(() => {
        using var db = this.OpenReader();
        using var cmd = Command(db, """
            SELECT payload FROM card_cache WHERE source=$source AND query=$query AND item_id=$id AND kind=$kind AND page=$page
            AND ($version IS NULL OR version=$version) AND ($language IS NULL OR language=$language) AND ($scope IS NULL OR data_scope=$scope)
            ORDER BY captured_at DESC,rowid DESC LIMIT 1
            """, null, ("$source", source), ("$query", (int)query), ("$id", id), ("$kind", kind), ("$page", page),
            ("$version", version), ("$language", language), ("$scope", dataScope));
        if (cmd.ExecuteScalar() is not byte[] payload) return null;
        var result = MessagePackSerializer.Deserialize<ServerGameCard>(payload);
        return result.Valid ? result : null;
    });
    public Task SaveEquipmentAsync(string source, EquipmentSnapshot snapshot) {
        if (source.Length is 0 or > 256 || !CardProtocol.ValidEquipment(snapshot)) throw new ArgumentException("Invalid equipped-item snapshot.");
        var saved = MessagePackSerializer.Deserialize<EquipmentSnapshot>(MessagePackSerializer.Serialize(snapshot));
        saved.IsLive = false; // Persisted equipment is never a claim about a live login.
        var payload = MessagePackSerializer.Serialize(saved);
        if (payload.Length > CardProtocol.MaxPacketBytes) throw new ArgumentException("Equipment exceeds its budget.");
        return this.Enqueue((db, tx) => {
            using var cmd = Command(db, """
                INSERT INTO card_equipment(source,owner_key,captured_at,payload) VALUES($source,$owner,$time,$payload)
                ON CONFLICT(source,owner_key) DO UPDATE SET captured_at=excluded.captured_at,payload=excluded.payload
                WHERE excluded.captured_at>=card_equipment.captured_at;
                DELETE FROM card_equipment WHERE rowid NOT IN (SELECT rowid FROM card_equipment ORDER BY captured_at DESC LIMIT 128);
                """, tx, ("$source", source), ("$owner", saved.OwnerKey), ("$time", saved.CapturedAtUnixMilliseconds), ("$payload", payload));
            cmd.ExecuteNonQuery();
        });
    }
    public Task<EquipmentSnapshot?> GetEquipmentAsync(string source, string owner) => Task.Run(() => {
        using var db = this.OpenReader();
        using var cmd = Command(db, "SELECT payload FROM card_equipment WHERE source=$source AND owner_key=$owner", null, ("$source", source), ("$owner", owner));
        if (cmd.ExecuteScalar() is not byte[] payload) return null;
        var result = MessagePackSerializer.Deserialize<EquipmentSnapshot>(payload); result.IsLive = false;
        return CardProtocol.ValidEquipment(result) ? result : null;
    });
    public Task SaveCardFavoriteAsync(CardFavorite favorite, string? messageId = null) {
        if (favorite.Id.Length is 0 or > 128 || favorite.Source.Length is 0 or > 256 || favorite.OwnerKey.Length is 0 or > 128 ||
            favorite.Name.Length > 512 || favorite.Snapshot == null || (!favorite.IsMap && !CardProtocol.ValidItemData(favorite.Snapshot)) ||
            (favorite.IsMap && (favorite.Snapshot.MapId is not (> 0 and < 100_000) ||
                !CardProtocol.ValidMap(new CardMap { Id = favorite.Snapshot.MapId ?? 0, Name = favorite.Snapshot.MapPlaceName ?? "", Filename = favorite.Snapshot.MapFilenameId, X = favorite.Snapshot.MapX, Y = favorite.Snapshot.MapY })))) throw new ArgumentException("Invalid card favorite.");
        var payload = MessagePackSerializer.Serialize(favorite);
        if (payload.Length > CardProtocol.MaxPacketBytes) throw new ArgumentException("Favorite exceeds its budget.");
        return this.Enqueue((db, tx) => {
            using (var existing = Command(db, "SELECT COUNT(*) FROM favorites f LEFT JOIN card_favorites c ON f.id=c.id WHERE f.id=$id AND (c.id IS NULL OR c.source<>$source OR c.owner_key<>$owner)", tx,
                ("$id", favorite.Id), ("$source", favorite.Source), ("$owner", favorite.OwnerKey))) {
                if (Convert.ToInt64(existing.ExecuteScalar()) != 0) throw new ArgumentException("Favorite belongs to another source or owner.");
            }
            using var cmd = Command(db, """
                INSERT INTO favorites(id,kind,snapshot) VALUES($id,$kind,$payload)
                ON CONFLICT(id) DO UPDATE SET snapshot=excluded.snapshot;
                INSERT INTO card_favorites(id,source,owner_key,name,saved_at) VALUES($id,$source,$owner,$name,$time)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name,saved_at=excluded.saved_at;
                INSERT OR IGNORE INTO favorite_sources(favorite_id,message_id)
                SELECT $id,id FROM messages WHERE id=$message AND source=$source AND owner_key=$owner;
                """, tx, ("$id", favorite.Id), ("$kind", favorite.IsMap ? "map" : "item"), ("$payload", payload),
                ("$source", favorite.Source), ("$owner", favorite.OwnerKey), ("$name", favorite.Name), ("$time", favorite.SavedAt), ("$message", messageId));
            cmd.ExecuteNonQuery();
        });
    }
    public Task<IReadOnlyList<CardFavorite>> GetCardFavoritesAsync(string? source, string? owner, int offset = 0, int limit = 100,
        string? search = null, string? group = null, string? tag = null) => Task.Run<IReadOnlyList<CardFavorite>>(() => {
        using var db = this.OpenReader();
        using var cmd = Command(db, """
            SELECT f.snapshot,f.group_name,f.tags,f.note FROM card_favorites c JOIN favorites f ON f.id=c.id
            WHERE ($source IS NULL OR c.source=$source) AND ($owner IS NULL OR c.owner_key=$owner)
            AND ($search='' OR instr(lower(c.name || ' ' || f.note || ' ' || c.owner_key),lower($search))>0)
            AND ($group='' OR f.group_name=$group) AND ($tag='' OR instr(lower(f.tags),lower($tag))>0)
            ORDER BY c.saved_at DESC,c.id LIMIT $limit OFFSET $offset
            """, null, ("$source", source), ("$owner", owner), ("$limit", Math.Clamp(limit, 1, 100)), ("$offset", Math.Clamp(offset, 0, 100_000)),
            ("$search", search ?? ""), ("$group", group ?? ""), ("$tag", tag ?? ""));
        using var reader = cmd.ExecuteReader(); var rows = new List<CardFavorite>();
        while (reader.Read()) {
            var row = MessagePackSerializer.Deserialize<CardFavorite>((byte[])reader[0]);
            row.Group = reader.GetString(1); row.Tags = reader.GetString(2); row.Note = reader.GetString(3); rows.Add(row);
        }
        return rows;
    });
    public Task UpdateCardFavoriteMetadataAsync(string id, string source, string owner, string group, string tags, string note) {
        if (group.Length > 128 || tags.Length > 512 || note.Length > 8192) throw new ArgumentException("Favorite annotations exceed their limit.");
        return this.Enqueue((db, tx) => {
            using var cmd = Command(db, "UPDATE favorites SET group_name=$group,tags=$tags,note=$note WHERE id=$id AND EXISTS(SELECT 1 FROM card_favorites c WHERE c.id=$id AND c.source=$source AND c.owner_key=$owner)", tx,
                ("$id", id), ("$source", source), ("$owner", owner), ("$group", group), ("$tags", tags), ("$note", note)); cmd.ExecuteNonQuery();
        });
    }
    public Task<IReadOnlyList<HistoryRow>> GetCardSourcesAsync(string favorite, string source, string owner, int offset = 0) => Task.Run<IReadOnlyList<HistoryRow>>(() => {
        using var db = this.OpenReader();
        using var cmd = Command(db, """
            SELECT m.row_id,m.id,m.owner_key,m.payload,m.note,m.bookmarked FROM favorite_sources f JOIN messages m ON m.id=f.message_id
            JOIN card_favorites c ON c.id=f.favorite_id
            WHERE c.id=$id AND c.source=$source AND c.owner_key=$owner AND m.source=$source AND m.owner_key=$owner
            ORDER BY m.timestamp DESC,m.row_id DESC LIMIT 100 OFFSET $offset
            """, null, ("$id", favorite), ("$source", source), ("$owner", owner), ("$offset", Math.Clamp(offset, 0, 100_000)));
        using var reader = cmd.ExecuteReader(); var rows = new List<HistoryRow>();
        while (reader.Read()) rows.Add(new HistoryRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            MessagePackSerializer.Deserialize<ServerMessage>((byte[])reader[3]), reader.GetString(4), reader.GetBoolean(5), source));
        return rows;
    });
    public Task DeleteCardFavoriteAsync(string favorite, string source, string owner) => this.Enqueue((db, tx) => {
        using var cmd = Command(db, "DELETE FROM favorites WHERE id=$id AND EXISTS(SELECT 1 FROM card_favorites c WHERE c.id=$id AND c.source=$source AND c.owner_key=$owner)", tx,
            ("$id", favorite), ("$source", source), ("$owner", owner)); cmd.ExecuteNonQuery();
    });
}
