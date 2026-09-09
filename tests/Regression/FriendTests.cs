using System.Buffers;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatPlugin;
using XIVChatStorage;
using XIVChat_Desktop;

internal static class FriendTests {
    static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    static CharacterIdentity Owner(ulong id = 1) => new() { ContentId = id, Name = "Owner", HomeWorldId = 1 };
    static Player[] Players(int count) => Enumerable.Range(1, count).Select(i => new Player { ContentId = (ulong)i, Name = "Friend " + i, HomeWorld = 1 }).ToArray();
    internal static ServerPlayerList Snapshot(int count = 70) => new(PlayerListType.Friend, Players(count)) {
        Owner = Owner(), OwnerEpoch = "login1", SnapshotId = Guid.NewGuid().ToString("N"), CapturedAt = DateTime.UtcNow,
    };
    static byte[] FirstFields(byte[] bytes, int count) {
        var reader = new MessagePackReader(bytes); reader.ReadArrayHeader();
        var buffer = new ArrayBufferWriter<byte>(); var writer = new MessagePackWriter(buffer); writer.WriteArrayHeader(count);
        for (int i = 0; i < count; i++) writer.WriteRaw(reader.ReadRaw());
        writer.Flush(); return buffer.WrittenSpan.ToArray();
    }
    public static Task Protocol() {
        var snapshot = Snapshot(200);
        var legacy = ServerPlayerList.Decode(FirstFields(snapshot.Encode()[1..], 2));
        Check(legacy.RequestId == null && legacy.Owner == null && legacy.Players.Length == 200, "Old list changed");
        Check(MessagePackSerializer.Deserialize<Player>(FirstFields(MessagePackSerializer.Serialize(snapshot.Players[0]), 15)).ContentId == 0, "Old player gained CID");
        Check(!ServerCapabilities.Decode(FirstFields(new ServerCapabilities().Encode()[1..], 4)).FriendSnapshots, "Old server gained capability");
        var pages = FriendListProtocol.Pages(snapshot, "req");
        Check(pages.Length == 7 && pages.All(p => p.Encode().Length <= FriendListProtocol.MaxPageBytes), "Page budget");
        var assembler = new FriendListAssembler("req", "cid:1", "login1");
        for (int i = 0; i < pages.Length - 1; i++) Check(assembler.Add(pages[i]) == null, "Partial escaped");
        Check(assembler.Add(pages[^1])?.Players.Length == 200, "Full snapshot lost");
        assembler = new("req", "cid:1", "login1");
        Check(assembler.Add(pages[1]) == null && assembler.Failure == FriendListStatus.Failed, "Out of order accepted");
        assembler = new("req", "cid:1", "other-login"); assembler.Add(pages[0]);
        Check(assembler.Failure == FriendListStatus.Failed, "Old login accepted");
        var duplicate = FriendListProtocol.Pages(Snapshot(33), "dup"); duplicate[1].Players[0].ContentId = 1;
        assembler = new("dup", "cid:1", "login1"); assembler.Add(duplicate[0]); assembler.Add(duplicate[1]);
        Check(assembler.Failure == FriendListStatus.Failed, "Cross-page duplicate accepted");
        return Task.CompletedTask;
    }
    public static Task Session() {
        var state = new FriendListSession(); state.SetContext("a", "cid:1", "login1", true);
        var saved = Snapshot(); state.Restore(state.Version, saved);
        var req = state.Begin(true)!; var pages = FriendListProtocol.Pages(Snapshot(), req.RequestId);
        state.Add(pages[0]); Check(ReferenceEquals(saved, state.Snapshot), "Partial replaced cache");
        state.Fail(req.RequestId!, FriendListStatus.TimedOut); state.Add(pages[1]);
        Check(ReferenceEquals(saved, state.Snapshot) && state.IsStale, "Timeout lost cache");
        req = state.Begin(true)!;
        foreach (var page in FriendListProtocol.Pages(Snapshot(0), req.RequestId)) state.Add(page);
        Check(state.Snapshot?.Players.Length == 0 && !state.IsStale, "Successful empty list not published");
        state.SetContext("a", null, null, false);
        Check(state.Snapshot?.Owner?.Key == "cid:1" && state.IsStale, "Disconnect discarded identifiable stale snapshot");
        var oldVersion = state.Version; state.SetContext("a", "cid:2", "login2", true); state.Restore(oldVersion, saved);
        Check(state.Snapshot == null, "Previous role restored");
        state.SetContext("a", "cid:1", "login3", true); req = state.Begin(false)!;
        state.Add(pages[^1]); Check(state.RequestId == req.RequestId, "Late previous request ended current request");
        state.SetContext("b", "cid:1", "login3", true); Check(state.Snapshot == null, "Different source leaked");
        return Task.CompletedTask;
    }
    public static Task Coordinator() {
        var reader = new Reader(); var replies = new List<(Guid Id, ServerPlayerList Page)>();
        using var coordinator = new FriendListCoordinator(reader, (r, p) => replies.Add((r.ClientId, p)));
        var now = DateTime.UtcNow;
        FriendRequest Request(string epoch = "login1") => new(Guid.NewGuid(), new ClientPlayerList { Type = PlayerListType.Friend, RequestId = Guid.NewGuid().ToString("N"), ExpectedOwnerKey = "cid:1", ExpectedOwnerEpoch = epoch }, default);
        coordinator.Enqueue(Request()); coordinator.Enqueue(Request()); coordinator.Tick(Owner(), "login1", now);
        Check(reader.Starts == 1 && replies.Count == 0, "Concurrent requests not coalesced");
        reader.Result = new("login1", Players(70), FriendListStatus.Success); coordinator.Tick(Owner(), "login1", now.AddSeconds(1));
        Check(replies.Count == 6 && replies.Select(r => r.Id).Distinct().Count() == 2, "Missing client/pages");
        replies.Clear(); coordinator.Enqueue(Request()); coordinator.Tick(Owner(), "login1", now.AddSeconds(2));
        Check(reader.Starts == 1 && replies.Count == 3, "Cache did not coalesce");
        replies.Clear(); coordinator.Enqueue(Request()); coordinator.Tick(Owner(), "login1", now.AddSeconds(7));
        coordinator.Tick(Owner(), "login1", now.AddSeconds(18));
        Check(replies.Single().Page.Status == FriendListStatus.TimedOut, "No timeout reply");
        replies.Clear(); coordinator.Enqueue(Request()); coordinator.Tick(Owner(), "login1", now.AddSeconds(19));
        coordinator.Tick(Owner(2), "login2", now.AddSeconds(20)); reader.Result = new("login1", Players(5), FriendListStatus.Success);
        coordinator.Tick(Owner(2), "login2", now.AddSeconds(21));
        Check(replies.Single().Page.Status == FriendListStatus.IdentityChanged && coordinator.LastCount == 0, "Old role leaked");
        replies.Clear(); coordinator.Enqueue(Request()); coordinator.Tick(null, "logout", now.AddSeconds(22));
        Check(replies.Single().Page.Status == FriendListStatus.NotLoggedIn, "Logged out returned snapshot");
        for (int i = 0; i < 256; i++) Check(coordinator.Enqueue(Request()), "Queue too small");
        Check(!coordinator.Enqueue(Request()), "Queue unbounded");
        return Task.CompletedTask;
    }
    public static async Task Storage() {
        var dir = Path.Combine(Path.GetTempPath(), "xivchat-friends-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.db");
        try {
            await using (var store = await HistoryStore.OpenAsync(path)) await store.AppendAsync("source", WorkbenchTests.Message(1), "id");
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False")) {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DROP TABLE friend_snapshots; PRAGMA user_version=1"; cmd.ExecuteNonQuery();
            }
            await using (var store = await HistoryStore.OpenAsync(path)) {
                Check((await store.SearchAsync(new())).Count == 1, "Migration lost messages");
                var snapshot = Snapshot(); await store.SaveFriendSnapshotAsync("source", snapshot);
                Check((await store.GetFriendSnapshotAsync("source", "cid:1"))?.Players.Length == 70, "Snapshot missing");
                Check(await store.GetFriendSnapshotAsync("other", "cid:1") == null && await store.GetFriendSnapshotAsync("source", "cid:2") == null, "Partition leak");
                var older = Snapshot(0); older.CapturedAt = snapshot.CapturedAt.AddMinutes(-1); await store.SaveFriendSnapshotAsync("source", older);
                Check((await store.GetFriendSnapshotAsync("source", "cid:1"))?.Players.Length == 70, "Older overwrote newer");
                var empty = Snapshot(0); empty.CapturedAt = snapshot.CapturedAt.AddSeconds(1); await store.SaveFriendSnapshotAsync("source", empty);
                Check((await store.GetFriendSnapshotAsync("source", "cid:1"))?.Players.Length == 0, "Empty not persisted");
                try { await store.SaveFriendSnapshotAsync("source", FriendListProtocol.Pages(snapshot, "req")[0]); throw new Exception("Partial saved"); }
                catch (ArgumentException) { }
            }
            var backup = Directory.GetFiles(dir, "*.before-v2-*.bak").Single();
            using var backupDb = new SqliteConnection($"Data Source={backup};Pooling=False"); backupDb.Open();
            using var version = backupDb.CreateCommand(); version.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt32(version.ExecuteScalar()) == 1, "Backup is not pre-migration");
        } finally { Directory.Delete(dir, true); }
    }
    sealed class Reader : IFriendListReader {
        public int Starts; public FriendReadResult? Result;
        public void SetContext(CharacterIdentity? owner, string epoch) { }
        public FriendListStatus? BeginRefresh() { Starts++; return null; }
        public FriendReadResult? TakeResult() { var result = Result; Result = null; return result; }
        public void Cancel() { Result = null; }
        public void Dispose() { }
    }
}
