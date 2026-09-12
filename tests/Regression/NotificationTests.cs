using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;
using XIVChat_Desktop;
using XIVChatPlugin;

internal static class NotificationTests {
    sealed class TempDatabase : IDisposable {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xivchat-events-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "history.db");
        public TempDatabase() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    internal const string DowngradeToV3 = "DROP TABLE card_cache; DROP TABLE card_equipment; DROP TABLE card_favorites; DROP INDEX events_source_owner_time; DROP INDEX events_time; ALTER TABLE events DROP COLUMN source; ALTER TABLE events DROP COLUMN is_read; PRAGMA user_version=3;";
    static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    static CharacterIdentity Owner(ulong id = 1) => new() { ContentId = id, Name = "Owner Name", HomeWorldId = 1, HomeWorld = "Home" };
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static NotificationCandidate Candidate(string id, NotificationKind kind = NotificationKind.Tell, string source = "s", string owner = "cid:1") =>
        new(id, kind, new(NotificationTargetKind.Conversation, source, owner, Peer: Owner(9)), "Peer", "消息", Now);
    static IReadOnlyList<NotificationDelivery> Drain(NotificationPolicy policy, NotificationOptions options, int seconds = 2, bool reading = false) =>
        policy.Drain(options, Now.AddSeconds(seconds), Now.AddSeconds(seconds), _ => reading);

    public static Task Policy() {
        var options = new NotificationOptions(); var policy = new NotificationPolicy();
        Check(!policy.Enqueue(Candidate("read"), options, Now, Now, true), "Foreground reading alerted");
        Check(!policy.Enqueue(Candidate("read"), options, Now, Now), "Suppressed input replayed");
        Check(!policy.Enqueue(Candidate("old") with { Timestamp = Now.AddMinutes(-3) }, options, Now, Now), "Replay alerted");
        Check(!policy.Enqueue(Candidate("future") with { Timestamp = Now.AddMinutes(1) }, options, Now, Now), "Future event alerted");
        Check(!policy.Enqueue(Candidate("expired", NotificationKind.DutyReady) with { ExpiresAt = Now }, options, Now, Now), "Expired duty alerted");
        policy.Enqueue(Candidate("duty", NotificationKind.DutyReady) with { ExpiresAt = Now.AddSeconds(45) }, options, Now, Now, true);
        Check(Drain(policy, options, 0, true).Single().Candidate.Kind == NotificationKind.DutyReady, "Reading suppressed duty");
        options.DoNotDisturb = true;
        Check(!policy.Enqueue(Candidate("quiet", NotificationKind.DutyReady), options, Now, Now), "DND allowed duty");
        options.DoNotDisturb = false;
        Check(!policy.Enqueue(Candidate("quiet", NotificationKind.DutyReady), options, Now, Now), "DND catch-up occurred");
        policy.Enqueue(Candidate("pending"), options, Now, Now); options.DoNotDisturb = true;
        Check(Drain(policy, options).Count == 0 && policy.PendingCount == 0, "Pending alert ignored DND change");
        options.DoNotDisturb = false; options.Tell = false;
        Check(!policy.Enqueue(Candidate("disabled"), options, Now, Now), "Disabled category alerted");
        Check(!policy.Enqueue(Candidate("login", NotificationKind.LoginLogout), options, Now, Now), "Login alerted by default");
        options.Tell = true; policy.Enqueue(Candidate("readlater"), options, Now, Now);
        Check(Drain(policy, options, reading: true).Count == 0, "Reading before delivery still alerted");
        policy.Enqueue(Candidate("wrongrole"), options, Now, Now);
        Check(policy.Drain(options, Now.AddSeconds(2), Now, _ => false, _ => false).Count == 0, "Old connection / role survived applicability check");
        options.QuietHours = true;
        Check(options.IsQuiet(Now.Date.AddHours(22)) && options.IsQuiet(Now.Date.AddHours(7)) && !options.IsQuiet(Now.Date.AddHours(8)) && !options.IsQuiet(Now), "Overnight quiet hours boundaries");
        options.QuietStart = TimeSpan.FromHours(10); options.QuietEnd = TimeSpan.FromHours(14);
        Check(options.IsQuiet(Now) && !options.IsQuiet(Now.Date.AddHours(14)), "Daytime quiet hours boundaries");
        options.QuietEnd = options.QuietStart; Check(options.IsQuiet(Now), "Equal quiet hours should cover the day");
        options.QuietStart = TimeSpan.FromDays(1); Check(!options.ValidTimes, "Invalid schedule accepted");
        return Task.CompletedTask;
    }

    public static Task Grouping() {
        var options = new NotificationOptions(); var policy = new NotificationPolicy();
        for (int i = 0; i < 20; i++) Check(policy.Enqueue(Candidate(i.ToString()), options, Now, Now), "Burst rejected");
        Check(Drain(policy, options, 1).Count == 0, "Burst delivered before collection window");
        var first = Drain(policy, options).Single();
        Check(first.Count == 20 && first.Candidate.Id == "19" && first.Sound, "Burst count, newest message or sound lost");
        Check(!policy.Enqueue(Candidate("19"), options, Now, Now), "Duplicate alerted");
        policy.Enqueue(Candidate("next"), options, Now.AddSeconds(3), Now);
        Check(Drain(policy, options, 11).Count == 0 && Drain(policy, options, 12).Single().Tag == first.Tag, "Conversation throttle/tag unstable");
        policy.Enqueue(Candidate("a", source: "other"), options, Now, Now);
        policy.Enqueue(Candidate("b", owner: "cid:2"), options, Now, Now);
        var separate = Drain(policy, options, 13);
        Check(separate.Count == 2 && separate.Select(d => d.Tag).Distinct().Count() == 2 && separate.All(d => !d.Sound), "Partitions merged or sound burst escaped");
        options.Sound = false; policy.Enqueue(Candidate("silent", NotificationKind.DutyReady), options, Now, Now);
        Check(!Drain(policy, options, 14).Single().Sound, "Sound disable ignored");
        for (int i = 0; i < 5000; i++) policy.Enqueue(Candidate("bounded" + i, owner: "cid:" + i), options, Now, Now);
        Check(policy.PendingCount == 128 && Drain(policy, options).Count == 32, "Pending or per-tick budget unbounded");
        return Task.CompletedTask;
    }

    internal static ServerGameEvent Event(string id = "event", ulong owner = 1) => new() {
        EventId = id, ServiceId = "service", RunId = "run", Owner = Owner(owner), OwnerEpoch = "login",
        Kind = GameEventKind.DutyReady, Timestamp = Now, ExpiresAt = Now.AddSeconds(45), Name = "测试副本", DataId = 42,
    };
    public static Task Protocol() {
        var entry = Event(); var decoded = ServerGameEvent.Decode(entry.Encode()[1..]);
        Check(decoded.IsValid(true) && decoded.Name == entry.Name && decoded.Owner?.Key == entry.Owner!.Key, "Event wire round-trip");
        entry.ExpiresAt = null; Check(!entry.IsValid(true), "Duty without expiry accepted");
        entry.ExpiresAt = Now.AddMinutes(2); Check(!entry.IsValid(true), "Unbounded duty expiry accepted");
        entry = Event(); entry.Owner = null; Check(!entry.IsValid(true), "Unowned plugin event accepted");
        entry = Event(); entry.Kind = GameEventKind.ConnectionLost; Check(!entry.IsValid(true) && entry.IsValid(), "Plugin forged local event");
        entry = Event(); entry.Name = new string('x', 257); Check(!entry.IsValid(), "Unbounded event accepted");
        Check(!ServerCapabilities.Decode(MessagePackSerializer.Serialize(new object[] { "service", "run", true, true })).GameEvents, "Legacy server gained event support");
        var target = new NotificationTarget(NotificationTargetKind.Conversation, "source", "cid:1", "row", Owner(9), 13);
        var parsed = NotificationTarget.Parse(target.ToArgument());
        Check(parsed?.OwnerKey == target.OwnerKey && parsed.Peer?.Key == target.Peer!.Key && parsed.RecordId == "row", "Activation lost identity");
        Check(NotificationTarget.Parse("!") == null && NotificationTarget.Parse(new string('a', 4097)) == null &&
            NotificationTarget.Parse((target with { Kind = (NotificationTargetKind)99 }).ToArgument()) == null, "Malformed activation accepted");
        return Task.CompletedTask;
    }

    public static async Task GameQueue() {
        var queue = new GameEventQueue(); var context = new GameCommandContext("cid:1", "login1", 0);
        var accepted = 0;
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => {
            if (queue.TryEnqueue((uint)i, new string('x', 300), Now, context)) Interlocked.Increment(ref accepted);
        })));
        Check(accepted == 8, "Concurrent duty queue exceeded its bound");
        var ready = queue.Drain("cid:1", "login1");
        Check(ready.Count == 8 && ready.Select(e => e.DataId).Distinct().Count() == 8 && ready.All(e => e.Name.Length == 256), "Cross-thread duty metadata changed");
        queue.TryEnqueue(1, "old login", Now, context); Check(queue.Drain("cid:1", "login2").Count == 0, "Previous login duty escaped");
        queue.TryEnqueue(2, "old owner", Now, context); Check(queue.Drain("cid:2", "login1").Count == 0, "Previous owner duty escaped");
        Check(!queue.TryEnqueue(3, "logged out", Now, context with { OwnerKey = null }), "Unowned duty accepted");
        queue.TryEnqueue(4, "logout", Now, context); queue.Clear(); Check(queue.Drain("cid:1", "login1").Count == 0, "Logout retained queued duty");
        queue.Complete(); Check(!queue.TryEnqueue(5, "disposed", Now, context), "Disposed server accepted callback");
    }

    public static async Task Storage() {
        using var temp = new TempDatabase();
        await using (var store = await HistoryStore.OpenAsync(temp.Path)) await store.AppendAsync("s", WorkbenchTests.Message(1), "keep");
        using (var sql = new SqliteConnection($"Data Source={temp.Path};Pooling=False")) {
            sql.Open(); using var cmd = sql.CreateCommand(); cmd.CommandText = DowngradeToV3 + "INSERT INTO events VALUES('reserved','cid:1',1,'legacy',X'00');"; cmd.ExecuteNonQuery();
        }
        await using (var store = await HistoryStore.OpenAsync(temp.Path)) {
            Check((await store.SearchAsync(new())).Single().Id == "keep" && (await store.GetEventsAsync(new())).Count == 0, "Migration lost history or exposed legacy payload");
            Check(await store.AppendEventAsync("a", Event()) && !await store.AppendEventAsync("a", Event()), "Event deduplication failed");
            await store.AppendEventAsync("b", Event()); await store.AppendEventAsync("a", Event("other-owner", 2));
            Check((await store.GetEventsAsync(new("a", "cid:1"))).Count == 1 && (await store.GetEventOwnersAsync()).Count == 3, "Event owner/source isolation failed");
            for (int i = 0; i < 250; i++) {
                var entry = Event("page/" + i.ToString("D3")); entry.Timestamp = Now.AddTicks(i); await store.AppendEventAsync("page", entry);
            }
            var ids = new HashSet<string>(); var query = new EventQuery("page", Limit: 37);
            while (true) {
                var page = await store.GetEventsAsync(query); if (page.Count == 0) break;
                foreach (var row in page) Check(ids.Add(row.Id), "Pagination repeated a same-millisecond row");
                query = query with { BeforeTimestampUtc = page[^1].Event.Timestamp, BeforeId = page[^1].Id };
            }
            Check(ids.Count == 250 && await store.GetUnreadEventCountAsync("page", "cid:1") == 250, "Pagination lost same-millisecond rows");
            await store.MarkEventsReadAsync(ids.Take(20)); Check(await store.GetUnreadEventCountAsync("page", "cid:1") == 230, "Read marker not durable");
            Check((await store.GetEventAsync(ids.First()))?.Read == true, "Single event read state differs");
            await store.PruneAsync(0, Now.AddDays(100)); Check((await store.GetEventsAsync(new("a"))).Count == 2, "Forever retention deleted events");
            await store.PruneAsync(90, Now.AddDays(100)); Check((await store.GetEventsAsync(new())).Count == 0 && await store.GetUnreadEventCountAsync(null, null) == 0, "Retention kept old events or unread count");
        }
        Check(Directory.GetFiles(temp.Directory, "*.before-v5-*.bak").Length == 1, "Pre-v5 backup missing");
    }
}
