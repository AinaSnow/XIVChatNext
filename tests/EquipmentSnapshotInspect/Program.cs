using MessagePack;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using XIVChatCommon;

// Read only: inspect equipment actually persisted by the connected desktop client.
var path = args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChatDesktop", "history.sqlite3");
using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
db.Open();
using var command = db.CreateCommand();
command.CommandText = "SELECT payload FROM card_equipment ORDER BY captured_at DESC LIMIT 1";
if (command.ExecuteScalar() is not byte[] payload) { Console.Error.WriteLine("No equipment snapshot has arrived."); return 1; }
var snapshot = MessagePackSerializer.Deserialize<EquipmentSnapshot>(payload);
Console.WriteLine(JsonSerializer.Serialize(new {
    Valid = CardProtocol.ValidEquipment(snapshot), snapshot.ClassJobId, snapshot.Level, snapshot.Revision,
    CapturedAt = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.CapturedAtUnixMilliseconds).ToLocalTime(),
    snapshot.IsLevelSynced,
    Note = "Persisted snapshots are marked non-live; verify current connection and UI separately.",
    Items = snapshot.Items.Select(i => new { i.Slot, i.Item.ItemId, i.Item.ItemName, i.Item.ItemKind, i.HasCustomStats,
        i.Item.ItemDetails, i.Materia })
}, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
return CardProtocol.ValidEquipment(snapshot) ? 0 : 2;
