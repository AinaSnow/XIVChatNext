using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatCommon.Presentation;
using XIVChatStorage;

var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; }
void Equal<T>(T actual, T expected, string label) => Check(EqualityComparer<T>.Default.Equals(actual, expected), label + $" (expected {expected}; got {actual})");
void Throws<T>(Action action, string label) where T : Exception {
    try { action(); throw new Exception("FAIL: " + label); } catch (T) { checks++; }
}
async Task ThrowsAsync<T>(Func<Task> action, string label) where T : Exception {
    try { await action(); throw new Exception("FAIL: " + label); } catch (T) { checks++; }
}
CharacterIdentity Person(string name, ushort world = 1, ulong cid = 0) => new() { Name = name, HomeWorldId = world, HomeWorld = world == 1 ? "Alpha" : "Beta", ContentId = cid };
var self = Person("Alice Snow", cid: 101);
var peer = Person("Bob Birch", cid: 202);
var other = Person("Cora Star", 2, 303);
var settings = new PrivacySettings();
var view = new IdentityDisplay(settings);
var scope = view.Context("fixture", self.Key, self);
view.Register(scope, peer); view.Register(scope, other);
Equal(view.Identity(scope, self).Name, self.Name, "normal self name");
view.Restore(view.WithNickname(scope, peer, "  Teammate  "));
Equal(view.Identity(scope, peer).Name, "Teammate", "nickname trimmed and displayed");
Equal(view.Identity(scope, peer).World, "Alpha", "ordinary world retained");
Equal(view.Identity(scope, Person(peer.Name, cid: 999)).Name, "Teammate", "CID enrichment preserves name-world alias");
Equal(view.Identity(scope, Person(peer.Name, 2)).Name, peer.Name, "different world does not reuse nickname");
Equal(view.Identity(view.Context("other-source", self.Key, self), peer).Name, peer.Name, "source partition");
Equal(view.Identity(view.Context("fixture", other.Key, other), peer).Name, peer.Name, "owner partition");
Throws<ArgumentException>(() => view.WithNickname(scope, peer, "first\nsecond"), "multiline nickname rejected");
Throws<ArgumentException>(() => view.WithNickname(scope, peer, new string('a', 65)), "long nickname rejected");
Throws<ArgumentException>(() => view.WithNickname(scope, new() { Name = "Unknown" }, "Name"), "incomplete target not persisted");

ServerMessage Message(string text, string? sender = null) => new(DateTime.UtcNow, ChatType.TellIncoming,
    Encoding.UTF8.GetBytes(sender ?? peer.Name), Encoding.UTF8.GetBytes(text), new() {
        new TextChunk("[Tell] "), new TextChunk("Bob ") { Foreground = 0xabcdef00 },
        new TextChunk("Birch: "), new TextChunk(text) { ItemId = 42, ItemName = "A harmless item" }
    }) { Owner = self, TellPeer = peer, MessageId = Guid.NewGuid().ToString("N"), LocalSource = "fixture", Sequence = 1 };
var message = Message("Alice Snow: hello Bob Birch @ Alpha and Cora Star!");
var original = MessagePackSerializer.Serialize(message);
var plainLine = HistoryExport.Line(message, false);
Equal(view.ExportLine("fixture", message, false), plainLine, "normal export stays original despite nickname");
var normalChunks = string.Concat(view.Chunks("fixture", message).OfType<TextChunk>().Select(c => c.Content));
Check(normalChunks.StartsWith("[Tell] Teammate: "), "nickname in sender across chunks");
Check(normalChunks.EndsWith(message.ContentText), "nickname does not rewrite ordinary body");

settings.Enabled = true; view.Configure(settings, false);
var generated = new List<ContactDisplayProfile>(); view.ProfileGenerated += generated.Add;
var alias = view.Identity(scope, peer);
Equal(view.Identity(scope, self).Name, "Me", "default self alias");
Check(alias.Hidden && alias.World == "" && alias.Name != peer.Name && alias.Name != "Teammate", "privacy overrides nickname and world");
Check(view.Identity(scope, peer).Name == alias.Name, "stable repeated pseudonym");
Check(!view.Text(scope, message.ContentText).Contains("Bob Birch") && !view.Text(scope, message.ContentText).Contains("Alpha"), "body and linked world redacted");
Equal(view.Text(scope, "Bob Birchwood and XBob Birch"), "Bob Birchwood and XBob Birch", "partial names not corrupted");
Equal(view.Text(scope, "Alice Snowは戦利品を手に入れた。"), "Meは戦利品を手に入れた。", "Japanese loot text masks adjacent Latin character name");
Equal(view.Text(scope, "Alice Snowの攻撃"), "Meの攻撃", "Japanese combat text masks adjacent Latin character name");
Equal(view.Text(scope, "玩家Alice Snow获得了物品。"), "玩家Me获得了物品。", "Chinese text masks adjacent Latin character name");
Equal(view.Text(scope, "Alice Snowé Alice Snow\u0301"), "Alice Snowé Alice Snow\u0301", "accented and combining suffixes do not match partial names");
var cjkPeer = Person("星野光", cid: 505); view.Register(scope, cjkPeer);
Equal(view.Text(scope, "星野光子"), "星野光子", "longer CJK names do not match partial names");
var japaneseMessage = Message("Alice Snowは戦利品を手に入れた。");
Check(!view.ExportLine("fixture", japaneseMessage, false).Contains(self.Name), "Japanese export masks character name");
Check(!string.Concat(view.Chunks("fixture", japaneseMessage).OfType<TextChunk>().Select(c => c.Content)).Contains(self.Name), "Japanese rendered and copied text masks character name");
Equal(view.Text(scope, "unknown nickname"), "unknown nickname", "unknown free text left alone");
var projected = view.Chunks("fixture", message).OfType<TextChunk>().ToArray();
var rendered = string.Concat(projected.Select(c => c.Content));
Check(!rendered.Contains(self.Name) && !rendered.Contains(peer.Name) && !rendered.Contains(other.Name), "all identified names removed across styled chunks");
Equal(projected[1].Foreground, (uint?)0xabcdef00, "style retained");
Equal(projected[^1].ItemId, (uint?)42, "item link metadata retained");
Check(original.SequenceEqual(MessagePackSerializer.Serialize(message)), "original message bytes unchanged");
Check(!view.ExportLine("fixture", message, true).Contains(peer.Name), "privacy export removes sender");
Equal(TellTarget.From(peer).Name, "Bob Birch", "real directed tell target unchanged");

var restartedSettings = JsonSerializer.Deserialize<PrivacySettings>(JsonSerializer.Serialize(settings))!;
var restarted = new IdentityDisplay(restartedSettings);
var restartScope = restarted.Context("fixture", self.Key, self);
foreach (var profile in generated) restarted.Restore(profile);
Equal(restarted.Identity(restartScope, peer).Name, alias.Name, "restart preserves pseudonym and enabled state");
var snapshot = view.Snapshot();
settings.Enabled = false; view.Configure(settings, false);
Check(snapshot.Identity(scope, peer).Hidden && !view.Identity(scope, peer).Hidden, "export snapshot isolated from live settings");
Equal(view.Identity(scope, peer).Name, "Teammate", "turning off restores nickname");
view.Restore(view.WithNickname(scope, peer, ""));
Equal(view.Identity(scope, peer).Name, peer.Name, "clearing nickname restores original");

settings.Enabled = true; settings.HideSelf = true; settings.HideOthers = false; settings.SelfName = "Host"; view.Configure(settings, false);
Equal(view.Identity(scope, self).Name, "Host", "custom self name");
Equal(view.Identity(scope, peer).Name, peer.Name, "self-only leaves others visible");
settings.HideSelf = false; settings.HideOthers = true; view.Configure(settings, false);
Equal(view.Identity(scope, self).Name, self.Name, "others-only leaves self visible");
Check(view.Identity(scope, peer).Hidden, "others-only hides peer");
Equal(view.Identity(scope, new() { Name = "Someone Unknown" }).Name, "Anonymous player", "incomplete identity never falls back to real name");
settings.HideSelf = true; view.Configure(settings, true);
var sameName = Person(peer.Name, 2, 404); view.Register(scope, sameName);
Equal(view.Text(scope, peer.Name), "匿名玩家", "ambiguous bare same-name mention uses neutral alias");
Check(view.Text(scope, peer.Name + "@Alpha") != view.Text(scope, peer.Name + "@Beta"), "qualified same-name players stay distinct");
Check(view.Identity(scope, peer).Name != view.Identity(scope, sameName).Name, "distinct worlds get different pseudonyms");
var collisionView = new IdentityDisplay(settings);
var collisionScope = collisionView.Context("fixture", self.Key, self);
var token = generated.First(p => p.PeerKey == ConversationIdentity.PeerKey(peer)).Pseudonym;
collisionView.Restore(new("fixture", self.Key!, ConversationIdentity.PeerKey(other)!, other, Pseudonym: token));
ContactDisplayProfile? collisionProfile = null; collisionView.ProfileGenerated += p => collisionProfile = p;
collisionView.Identity(collisionScope, peer);
Check(collisionProfile!.Pseudonym.StartsWith(token) && collisionProfile.Pseudonym.Length > token.Length, "collision extends token");

var directory = Path.Combine(Path.GetTempPath(), "xivchat-privacy-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "history.sqlite3");
try {
    await using (var store = await HistoryStore.OpenAsync(path)) {
        await store.AppendAsync("fixture", message, "original-message", true);
        await store.SaveConversationAsync(new("fixture", self.Key!, ConversationIdentity.PeerKey(peer)!, peer, Note: "Long original note"));
        var profile = new ContactDisplayProfile("fixture", self.Key!, ConversationIdentity.PeerKey(peer)!, peer, "Friend note", token);
        await store.SaveContactNicknameAsync(profile);
        await store.SaveContactPseudonymAsync(profile with { Nickname = "stale" });
        var stored = (await store.GetContactDisplayProfilesAsync()).Single();
        Equal(stored.Nickname, "Friend note", "late pseudonym write preserves latest nickname");
        await store.SaveContactNicknameAsync(profile with { Nickname = "Updated", Pseudonym = "" });
        stored = (await store.GetContactDisplayProfilesAsync()).Single();
        Equal(stored.Pseudonym, token, "nickname update preserves pseudonym");
        Equal((await store.GetConversationsAsync("fixture", self.Key!)).Single().State.Note, "Long original note", "long notes remain independent");
        var exporter = view.Snapshot();
        var output = new StringWriter();
        await store.ExportAsync(new(), output, false, false, displayLine: (row, time) => exporter.ExportLine(row.Source, row.Message, time));
        Check(!output.ToString().Contains(peer.Name) && !output.ToString().Contains(self.Name), "TXT export masked");
        output = new StringWriter();
        await store.ExportAsync(new(), output, true, false, displayLine: (row, time) => exporter.ExportLine(row.Source, row.Message, time));
        Check(output.ToString().StartsWith("{\\rtf") && !output.ToString().Contains(peer.Name), "RTF masked before escaping");
        Equal((await store.SearchAsync(new())).Single().Message.ContentText, message.ContentText, "export leaves database original intact");
        var prepared = false;
        await store.ExportAsync(new(), new StringWriter(), false, false,
            displayLine: (row, time) => { Check(prepared, "identity preparation runs before output"); return "safe"; },
            prepareRow: row => prepared = true);
        await ThrowsAsync<OperationCanceledException>(() => store.ExportAsync(new(), new StringWriter(), false, false, token: new CancellationToken(true)), "cancelled export fails before output");
        // Exercise bounded export over a realistic page boundary, with pre-indexed identities.
        for (int i = 0; i < 1200; i++) { var next = Message("Hello Alice Snow and Bob Birch."); await store.AppendAsync("fixture", next, "bulk-" + i, false); }
        var clock = Stopwatch.StartNew();
        using var counter = new CountingWriter();
        var count = await store.ExportAsync(new(Source: "fixture"), counter, false, false, displayLine: (row, time) => exporter.ExportLine(row.Source, row.Message, time),
            prepareRow: row => exporter.Observe(row.Source, row.Message));
        Equal(count, 1201L, "streaming export visits all rows");
        Check(counter.Characters > 0, "streaming writer receives data without retaining all lines");
        Console.WriteLine($"Streaming export: {count} rows, {clock.ElapsedMilliseconds} ms");
    }
    // Simulate an actual v5 file by removing only the additive v6 table.
    using (var db = new SqliteConnection("Data Source=" + path)) {
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE contact_display; PRAGMA user_version=5;"; command.ExecuteNonQuery();
    }
    await using (var upgraded = await HistoryStore.OpenAsync(path)) {
        Equal((await upgraded.GetContactDisplayProfilesAsync()).Count, 0, "v5 migration creates empty display profiles");
        Equal((await upgraded.GetMessageAsync("original-message"))!.Message.ContentText, message.ContentText, "v5 migration retains original history");
        Equal((await upgraded.GetConversationsAsync("fixture", self.Key!)).Single().State.Note, "Long original note", "migration retains old notes");
        Check(Directory.GetFiles(directory, "*.before-v6-*.bak").Length > 0, "migration backup created");
    }
    using (var db = new SqliteConnection("Data Source=" + path)) {
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version=5;"; command.ExecuteNonQuery();
    }
    await ThrowsAsync<SqliteException>(async () => { await using var failed = await HistoryStore.OpenAsync(path); }, "failed migration is rejected");
    using (var db = new SqliteConnection("Data Source=" + path)) {
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version";
        Equal(Convert.ToInt32(command.ExecuteScalar()), 5, "failed migration rolls version back");
        command.CommandText = "SELECT count(*) FROM messages WHERE id='original-message'";
        Equal(Convert.ToInt32(command.ExecuteScalar()), 1, "failed migration preserves history");
    }
    Console.WriteLine($"PASS: {checks} privacy and storage checks.");
} finally {
    SqliteConnection.ClearAllPools();
    // Only delete this test's newly-created, absolute, uniquely-named temporary directory.
    if (Path.GetDirectoryName(Path.GetFullPath(directory)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(directory, true);
}

sealed class CountingWriter : TextWriter {
    public override Encoding Encoding => Encoding.UTF8;
    public long Characters { get; private set; }
    public override void Write(string? value) => Characters += value?.Length ?? 0;
}
