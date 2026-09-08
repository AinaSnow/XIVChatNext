using System.Buffers;
using System.Text;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

internal static class WorkbenchTests {
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
    internal static ServerMessage Message(int sequence, string text = "今晚一起制作装备", ulong owner = 1) => new(
        DateTime.UtcNow.AddSeconds(sequence), ChatType.TellIncoming, Encoding.UTF8.GetBytes("Aina Snow"),
        Encoding.UTF8.GetBytes(text), [new TextChunk(text)]) {
        MessageId = $"service:run:{sequence}", ServiceId = "service", RunId = "run", Sequence = sequence,
        Owner = new CharacterIdentity { ContentId = owner, Name = "Same Name", HomeWorldId = (ushort)owner, HomeWorld = "Home" },
        TellPeer = new CharacterIdentity { Name = "Peer Name", HomeWorldId = 7, HomeWorld = "PeerWorld" },
    };

    public static Task ProtocolCompatibility() {
        var message = Message(1);
        var bytes = MessagePackSerializer.Serialize(message);
        var reader = new MessagePackReader(bytes);
        Check(reader.ReadArrayHeader() == 11, "Message fields must be appended");
        var legacy = TruncateArray(bytes, 5);
        var decodedOld = ServerMessage.Decode(legacy);
        Check(decodedOld.MessageId == null && decodedOld.Owner == null && decodedOld.Sequence == 0, "Legacy record gained an identity");
        Check(decodedOld.ContentText == message.ContentText, "Original fields changed");
        var oldReader = MessagePackSerializer.Deserialize<LegacyMessage>(bytes);
        Check(oldReader.Channel == message.Channel && oldReader.Content.SequenceEqual(message.Content), "Old decoder cannot read new messages");
        var modern = ServerMessage.Decode(bytes);
        Check(modern.Owner?.Key == "cid:1" && modern.TellPeer?.Key == "name:7:Peer Name", "Identity lost");
        var data = new PlayerData("Home", "Visiting", "Area", "Same Name") { Identity = message.Owner };
        Check(PlayerData.Decode(TruncateArray(MessagePackSerializer.Serialize(data), 9)).Identity == null, "Legacy player gained identity");
        Check(PlayerData.Decode(MessagePackSerializer.Serialize(data)).Identity?.HomeWorldId == 1, "Home world changed to visiting world");
        var prefs = new ClientPreferences { Preferences = new() { [ClientPreference.WorkbenchSupport] = true } };
        Check(ClientPreferences.Decode(prefs.Encode()[1..]).TryGetValue<bool>(ClientPreference.WorkbenchSupport, out var enabled) && enabled, "Capability flag did not roundtrip");
        return Task.CompletedTask;
    }

    private static byte[] TruncateArray(byte[] bytes, int fields) {
        var reader = new MessagePackReader(bytes);
        reader.ReadArrayHeader();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(fields);
        for (int i = 0; i < fields; i++) writer.WriteRaw(reader.ReadRaw());
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    public static Task CursorPages() {
        var history = Enumerable.Range(101, 600).Select(i => Message(i, new string('物', 200))).ToArray();
        var request = new ClientHistory { After = new HistoryCursor { ServiceId = "service", RunId = "run", Sequence = 5 } };
        var received = new List<long>();
        int pages = 0;
        do {
            var page = HistoryPager.Create(history, "service", "run", 700, request);
            Check(page.Encode().Length < 128_000 - 16, "Oversize history frame");
            Check(page.Messages.Length <= 200, "Unbounded history page");
            Check(page.HasGap == (pages == 0), "Gap indication incorrect");
            received.AddRange(page.Messages.Select(m => m.Sequence));
            pages++;
            Check(pages < 30, "Cursor failed to advance");
            if (!page.HasMore) break;
            request = new ClientHistory { After = page.Cursor, Through = page.Through };
        } while (true);
        Check(received.SequenceEqual(history.Select(m => m.Sequence)), "Pages lost or reordered messages");
        var reloaded = HistoryPager.Create([], "service", "new-run", 0, request);
        Check(reloaded.HasGap && !reloaded.HasMore && reloaded.Cursor.Sequence == 0, "Restart did not report gap");
        var cleared = HistoryPager.Create([], "service", "run", 800, request);
        Check(cleared.HasGap && !cleared.HasMore, "Empty retained history did not report gap");
        var huge = HistoryPager.Create([Message(1, new string('x', 200_000)), Message(2)], "service", "run", 2, new());
        Check(huge.HasGap && !huge.HasMore && huge.Messages.Single().Sequence == 2, "Oversize message stalls recovery");
        var interrupted = HistoryPager.Create([Message(1), Message(3)], "service", "run", 3, new());
        Check(interrupted.HasGap, "A gap inside a retained page was hidden");
        return Task.CompletedTask;
    }

    public static async Task Persistence() {
        using var temp = new TempDatabase();
        await using (var store = await HistoryStore.OpenAsync(temp.Path)) {
            var first = Message(1);
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.AppendAsync("key", first, HistoryStore.StorageId("key", first))));
            await store.AppendAsync("key", Message(2, owner: 2), "key/2");
            var legacy = Message(3); legacy.MessageId = null; legacy.Owner = null;
            await store.AppendAsync("key", legacy, HistoryStore.StorageId("key", legacy));
            await store.AppendAsync("key", legacy, HistoryStore.StorageId("key", legacy));
            Check((await store.SearchAsync(new())).Count == 4, "Deduplication guessed legacy identity or duplicated stable identity");
            Check((await store.SearchAsync(new(OwnerKey: "cid:1"))).Count == 1, "Own-character isolation failed");
            Check((await store.SearchAsync(new(OwnerKey: "unassigned:key"))).Count == 2, "Unassigned history merged into a character");
            await store.SaveCursorAsync("key", new HistoryCursor { ServiceId = "service", RunId = "run", Sequence = 2 });
            Check(await store.GetCursorAsync("different-key", "service") == null, "Cursor leaked between server keys");
        }
        await using var reopened = await HistoryStore.OpenAsync(temp.Path);
        Check((await reopened.SearchAsync(new())).Count == 4, "Reopen lost committed messages");
        Check((await reopened.GetCursorAsync("key", "service"))?.Sequence == 2, "Cursor not persisted");
    }

    public static async Task Retention() {
        using var temp = new TempDatabase();
        await using var store = await HistoryStore.OpenAsync(temp.Path);
        for (int i = 1; i <= 5; i++) {
            var message = Message(i);
            message.Timestamp = DateTime.UtcNow.AddDays(-100);
            await store.AppendAsync("key", message, "key/" + i);
        }
        await store.AnnotateAsync("key/1", "", true);
        await store.AnnotateAsync("key/2", "私人备注", false);
        await store.RetainSourceAsync("favorite", "item", [1], "key/3");
        await store.PruneAsync(0, DateTime.UtcNow);
        Check((await store.SearchAsync(new())).Count == 5, "Forever retention deleted messages");
        await store.PruneAsync(90, DateTime.UtcNow);
        var rows = await store.SearchAsync(new());
        Check(rows.Select(row => row.Id).Order().SequenceEqual(new[] { "key/1", "key/2", "key/3" }), "Protected sources were removed");
        Check((await store.SearchAsync(new(Text: "私人"))).Single().Id == "key/2", "Note index missing");
        await store.AnnotateAsync("key/2", "更新", false);
        Check((await store.SearchAsync(new(Text: "私人备注"))).Count == 0, "Stale FTS note remained");
    }

    public static async Task Recovery() {
        using var temp = new TempDatabase();
        var corrupt = Encoding.UTF8.GetBytes("This is not a SQLite database.");
        File.WriteAllBytes(temp.Path, corrupt);
        try { await using var bad = await HistoryStore.OpenAsync(temp.Path); throw new Exception("Corrupt database accepted"); }
        catch (SqliteException) { }
        Check(File.ReadAllBytes(temp.Path).SequenceEqual(corrupt), "Corrupt database overwritten");
        File.Delete(temp.Path);
        using (var db = new SqliteConnection($"Data Source={temp.Path};Pooling=False")) {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version=999; CREATE TABLE sentinel(value TEXT); INSERT INTO sentinel VALUES('keep');";
            cmd.ExecuteNonQuery();
        }
        var newer = File.ReadAllBytes(temp.Path);
        try { await using var bad = await HistoryStore.OpenAsync(temp.Path); throw new Exception("Future schema accepted"); }
        catch (InvalidDataException) { }
        Check(File.ReadAllBytes(temp.Path).SequenceEqual(newer), "Newer schema overwritten");
        using (var db = new SqliteConnection($"Data Source={temp.Path};Pooling=False")) {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version=0;"; cmd.ExecuteNonQuery();
        }
        await using var upgraded = await HistoryStore.OpenAsync(temp.Path);
        Check(Directory.GetFiles(temp.Directory, "*.bak").Length == 1, "Migration did not back up existing data");
    }

    public static async Task Search() {
        using var temp = new TempDatabase();
        await using var store = await HistoryStore.OpenAsync(temp.Path);
        await Task.WhenAll(Enumerable.Range(1, 1200).Select(i => {
            var message = Message(i, i % 2 == 0 ? "今天制作装备：皮靴" : "去薰衣草苗圃集合", (ulong)(i % 3 + 1));
            return store.AppendAsync("key", message, "key/" + i);
        }));
        Check((await store.SearchAsync(new(Text: "皮", Limit: 500))).Count == 500, "One-character Chinese search failed");
        Check((await store.SearchAsync(new(Text: "皮靴", Limit: 500))).Count == 500, "Two-character Chinese search failed");
        Check((await store.SearchAsync(new(OwnerKey: "cid:1", Text: "制作装备", Channel: (ushort)ChatType.TellIncoming, Person: "Peer", Limit: 500))).Count == 200, "Combined FTS filters failed");
        Check((await store.SearchAsync(new(Text: "\" OR 1=1 --"))).Count == 0, "Query was interpreted as an expression");
        var page1 = await store.SearchAsync(new(Limit: 40));
        var page2 = await store.SearchAsync(new(BeforeRow: page1.Last().RowId, BeforeTimestampUtc: page1.Last().Message.Timestamp, Limit: 40));
        Check(!page1.Select(r => r.Id).Intersect(page2.Select(r => r.Id)).Any(), "Pagination repeats rows");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await store.SearchAsync(new(Text: "装备"), cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
    }

    public static async Task WriteFailure() {
        using var temp = new TempDatabase();
        await using var store = await HistoryStore.OpenAsync(temp.Path);
        await store.AppendAsync("key", Message(1), "key/1");
        await store.SaveCursorAsync("key", new HistoryCursor { ServiceId = "service", RunId = "run", Sequence = 1 });
        using (var db = new SqliteConnection($"Data Source={temp.Path};Pooling=False")) {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER fail_write BEFORE INSERT ON messages BEGIN SELECT RAISE(ABORT,'simulated write failure'); END;";
            cmd.ExecuteNonQuery();
        }
        try { await store.AppendAsync("key", Message(2), "key/2"); throw new Exception("Write error was swallowed"); }
        catch (SqliteException) { }
        Check((await store.SearchAsync(new())).Single().Id == "key/1", "Failed transaction changed existing messages");
        Check((await store.GetCursorAsync("key", "service"))?.Sequence == 1, "Failed write advanced cursor");
        try { await store.RetainSourceAsync("bad-favorite", "item", [1], "missing-message"); throw new Exception("Missing source accepted"); }
        catch (SqliteException) { }
        using var verify = new SqliteConnection($"Data Source={temp.Path};Pooling=False");
        verify.Open();
        using var query = verify.CreateCommand();
        query.CommandText = "SELECT count(*) FROM favorites";
        Check((long)query.ExecuteScalar()! == 0, "Failed favorite transaction partially committed");
    }

    public static async Task LargeHistory() {
        using var temp = new TempDatabase();
        await using var store = await HistoryStore.OpenAsync(temp.Path);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int start = 0; start < 100_000; start += 1000) {
            await Task.WhenAll(Enumerable.Range(start + 1, 1000).Select(i => {
                var message = Message(i, i % 20 == 0 ? "今晚制作工匠皮靴" : "今天一起冒险", (ulong)(i % 3 + 1));
                return store.AppendAsync("key", message, "key/" + i);
            }));
        }
        var written = timer.Elapsed;
        timer.Restart();
        var shortWord = await store.SearchAsync(new(Text: "皮靴", OwnerKey: "cid:1", Limit: 100));
        var phrase = await store.SearchAsync(new(Text: "工匠皮靴", OwnerKey: "cid:1", Limit: 100));
        Check(shortWord.Count == 100 && phrase.Count == 100, "Large Chinese queries lost results");
        Check(shortWord.Select(row => row.Id).SequenceEqual(phrase.Select(row => row.Id)), "Short/FTS search disagree");
        var last = phrase.Last();
        var next = await store.SearchAsync(new(Text: "工匠皮靴", OwnerKey: "cid:1", BeforeRow: last.RowId,
            BeforeTimestampUtc: last.Message.Timestamp, Limit: 100));
        Check(next.Count == 100 && !next.Select(row => row.Id).Intersect(phrase.Select(row => row.Id)).Any(), "Large history pagination overlaps");
        Console.WriteLine($"INFO 100,000 rows: write={written.TotalSeconds:F2}s; three filtered queries={timer.Elapsed.TotalMilliseconds:F0}ms");
    }

    private sealed class TempDatabase : IDisposable {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xivchat-history-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(this.Directory, "history.db");
        public TempDatabase() => System.IO.Directory.CreateDirectory(this.Directory);
        public void Dispose() => System.IO.Directory.Delete(this.Directory, true);
    }
}

[MessagePackObject]
public sealed class LegacyMessage {
    [Key(0)] [MessagePackFormatter(typeof(MillisecondsDateTimeFormatter))] public DateTime Timestamp { get; set; }
    [Key(1)] public ChatType Channel { get; set; }
    [Key(2)] public byte[] Sender { get; set; } = [];
    [Key(3)] public byte[] Content { get; set; } = [];
    [Key(4)] public List<Chunk> Chunks { get; set; } = [];
}
