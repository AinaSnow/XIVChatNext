using MessagePack;
using XIVChatCommon.Message;
using XIVChatCommon.Presentation;

namespace XIVChatStorage;

public sealed partial class HistoryStore {
    private static void MigrateContactDisplay(Microsoft.Data.Sqlite.SqliteConnection db, Microsoft.Data.Sqlite.SqliteTransaction tx) => Execute(db, """
        CREATE TABLE contact_display (
            source TEXT NOT NULL, owner_key TEXT NOT NULL, peer_key TEXT NOT NULL,
            peer BLOB NOT NULL, nickname TEXT NOT NULL DEFAULT '', pseudonym TEXT NOT NULL DEFAULT '',
            PRIMARY KEY(source, owner_key, peer_key));
        CREATE UNIQUE INDEX contact_display_pseudonym ON contact_display(source, owner_key, pseudonym) WHERE pseudonym != '';
        """, tx);

    public Task<IReadOnlyList<ContactDisplayProfile>> GetContactDisplayProfilesAsync() => Task.Run<IReadOnlyList<ContactDisplayProfile>>(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT source,owner_key,peer_key,peer,nickname,pseudonym FROM contact_display", null);
        using var reader = cmd.ExecuteReader();
        var result = new List<ContactDisplayProfile>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            MessagePackSerializer.Deserialize<CharacterIdentity>((byte[])reader[3]), reader.GetString(4), reader.GetString(5)));
        return result;
    });

    // Nickname edits and lazy pseudonym writes own separate columns, so late writes cannot
    // overwrite a newer nickname. The same identity partition is used by conversations.
    public Task SaveContactNicknameAsync(ContactDisplayProfile profile) => SaveContactDisplayAsync(profile, nickname: true);
    public Task SaveContactPseudonymAsync(ContactDisplayProfile profile) => SaveContactDisplayAsync(profile, nickname: false);

    private Task SaveContactDisplayAsync(ContactDisplayProfile profile, bool nickname) {
        if (string.IsNullOrEmpty(profile.Source) || string.IsNullOrEmpty(profile.OwnerKey) ||
            ConversationIdentity.PeerKey(profile.Peer) != profile.PeerKey || profile.Peer.Name.Length > 128 || profile.Peer.HomeWorld.Length > 64)
            throw new ArgumentException("Invalid contact identity.");
        var name = PrivacySettings.NormalizeName(profile.Nickname);
        if (profile.Pseudonym.Length > 64 || profile.Pseudonym.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid pseudonym token.");
        var peer = MessagePackSerializer.Serialize(profile.Peer);
        return Enqueue((db, tx) => {
            using var cmd = Command(db, """
                INSERT INTO contact_display(source,owner_key,peer_key,peer,nickname,pseudonym) VALUES($source,$owner,$key,$peer,$name,$token)
                ON CONFLICT(source,owner_key,peer_key) DO UPDATE SET peer=excluded.peer,
                nickname=CASE WHEN $editName=1 THEN excluded.nickname ELSE contact_display.nickname END,
                pseudonym=CASE WHEN $editName=0 THEN excluded.pseudonym ELSE contact_display.pseudonym END
                """, tx, ("$source", profile.Source), ("$owner", profile.OwnerKey), ("$key", profile.PeerKey), ("$peer", peer),
                ("$name", nickname ? name : ""), ("$token", nickname ? "" : profile.Pseudonym), ("$editName", nickname ? 1 : 0));
            cmd.ExecuteNonQuery();
        });
    }
}
