using System.Text;
using Microsoft.Data.Sqlite;
using XIVChatStorage;

internal static class Phase7Tests {
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid layout was accepted");
    }
    public static Task LayoutValidation() {
        var work = new WindowBounds(-1920, 40, 1920, 1040);
        var fit = new WindowBounds(100000, -100000, int.MaxValue, -1).Fit(work);
        Check(fit == new WindowBounds(-1920, 40, 1920, 300), "Off-screen / invalid sizes were not fitted to the monitor");
        Check(new WindowBounds(-1900, 50, 600, 500).Fit(work) == new WindowBounds(-1900, 50, 600, 500), "Valid negative-monitor coordinates changed");
        var state = new ChatWindowState { Source = "server/a", OwnerKey = "cid:1", ChannelId = "channel", Draft = "中文草稿 😀", PendingDraft = "待确认", Open = true, Bounds = fit };
        var layout = new WorkspaceLayout { MainVisible = false, Windows = [state] };
        var restored = WorkspaceLayout.Parse(layout.Serialize());
        Check(!restored.MainVisible && restored.Windows[0].Draft == state.Draft && restored.Windows[0].PendingDraft == state.PendingDraft && restored.Windows[0].Bounds == fit, "Window identity, geometry or drafts changed");
        Reject(() => WorkspaceLayout.Parse("{\"Version\":99}"));
        Reject(() => WorkspaceLayout.Parse("{\"Windows\":null}"));
        Reject(() => (layout with { Windows = [state, state] }).Serialize());
        Reject(() => (layout with { Windows = [state with { Draft = new string('a', 16385) }] }).Serialize());
        Reject(() => (layout with { Windows = [state with { Peer = WorkbenchTests.Message(1).TellPeer }] }).Serialize());
        Reject(() => (layout with { Windows = Enumerable.Range(0, 13).Select(i => state with { ChannelId = i.ToString() }).ToList() }).Serialize());
        Reject(() => WorkspaceLayout.Parse(new string(' ', 1048577)));
        Check(state.Key != (state with { Source = "server", OwnerKey = "a/cid:1" }).Key, "Delimiter collision merged two roles");
        return Task.CompletedTask;
    }
    public static async Task LayoutStorage() {
        using var temp = new Temp(); await using var store = await HistoryStore.OpenAsync(temp.Path);
        var layout = new WorkspaceLayout { Windows = [new ChatWindowState { Source = "fixture", OwnerKey = "cid:1", ChannelId = "tab", Draft = "保存的草稿" }] };
        await store.SaveLayoutAsync("current", layout); await store.SaveLayoutAsync("装修采购", layout);
        Check((await store.GetLayoutNamesAsync()).SequenceEqual(new[] { "装修采购" }), "Startup layout leaked into named presets");
        Reject(() => store.SaveLayoutAsync("current", layout with { Version = 2 }));
        Check((await store.LoadLayoutAsync("current"))!.Windows[0].Draft == "保存的草稿", "Rejected save overwrote valid layout");
        using var db = new SqliteConnection("Data Source=" + temp.Path); await db.OpenAsync();
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE window_layouts SET payload='{invalid' WHERE id='current'"; await cmd.ExecuteNonQueryAsync();
        try { await store.LoadLayoutAsync("current"); throw new Exception("Corrupt layout silently accepted"); } catch (System.Text.Json.JsonException) { }
        cmd.CommandText = "SELECT payload FROM window_layouts WHERE id='current'";
        Check((string)(await cmd.ExecuteScalarAsync())! == "{invalid", "Load failure modified the source layout");
    }
    public static async Task ExportFiltersAndSnapshot() {
        using var temp = new Temp(); await using var store = await HistoryStore.OpenAsync(temp.Path);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int offset = 0; offset < 1200; offset += 200) await Task.WhenAll(Enumerable.Range(offset + 1, 200).Select(i => {
            var message = WorkbenchTests.Message(i, "中文皮靴 " + i.ToString("D4"), (ulong)(i % 2 + 1)); message.Timestamp = start.AddSeconds(i / 2);
            return store.AppendAsync(i % 3 == 0 ? "second" : "first", message, "id/" + i);
        }));
        using var plain = new StringWriter();
        long count = await store.ExportAsync(new(), plain, false, false);
        var lines = plain.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Check(count == 1200 && lines.Length == 1200, "Export stopped at a UI page limit");
        Check(lines[0].EndsWith("0001") && lines[^1].EndsWith("1200"), "Timestamp order was not chronological");
        var query = new HistoryQuery(Source: "first", OwnerKey: "cid:1", Text: "皮靴", FromUtc: start.AddSeconds(100), UntilUtc: start.AddSeconds(200));
        var expected = (await store.SearchAsync(query with { Limit = 500 })).Select(r => r.Message.ContentText).Reverse().ToArray();
        using var filtered = new StringWriter();
        Check(await store.ExportAsync(query, filtered, false, false) == expected.Length && expected.Length > 0, "Role/source/date/short-Chinese filter mismatch");
        Check(filtered.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[(line.IndexOf("中文", StringComparison.Ordinal))..]).SequenceEqual(expected), "Search and export disagree");
        await store.AnnotateAsync("id/200", "工匠收藏", true);
        using var bookmark = new StringWriter();
        Check(await store.ExportAsync(new(BookmarksOnly: true, Text: "收藏"), bookmark, false, true) == 1 && bookmark.ToString().Contains("0200"), "Bookmark/note filter omitted its matching record");
        bool mutated = false;
        using var snapshot = new CallbackWriter(() => {
            if (mutated) return; mutated = true;
            var late = WorkbenchTests.Message(1201, "late arrival"); late.Timestamp = start.AddSeconds(1000);
            store.AppendAsync("first", late, "late").GetAwaiter().GetResult();
        });
        Check(await store.ExportAsync(new(), snapshot, false, false) == 1200 && mutated, "Concurrent append leaked into export snapshot");
        using var fresh = new StringWriter(); Check(await store.ExportAsync(new(Text: "late arrival"), fresh, false, false) == 1, "Concurrent writer did not commit");
    }
    public static Task RtfEncoding() {
        Check(HistoryExport.EscapeRtf("a\\{b}\n\t中文😀") == "a\\\\\\{b\\}\\line \\tab \\u20013?\\u25991?\\u-10179?\\u-8704?", "RTF control/unicode escaping is invalid");
        var message = WorkbenchTests.Message(1, "正文");
        Check(!HistoryExport.Line(message, false).StartsWith(message.Timestamp.ToLocalTime().ToString("yyyy")), "Disabled timestamps still exported");
        Check(HistoryExport.Line(message, true).Contains(message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")), "Enabled timestamps omitted");
        return Task.CompletedTask;
    }
    internal sealed class CallbackWriter(Action callback) : TextWriter {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value) => callback();
    }
    private sealed class Temp : IDisposable {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xivchat-phase7-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "history.db");
        public Temp() => Directory.CreateDirectory(directory);
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
}
