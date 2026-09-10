using System.Net;
using System.Text;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatPlugin;
using XIVChatStorage;
using XIVChat_Desktop;

internal static class Phase3Tests {
    internal const string DowngradeToV2 = NotificationTests.DowngradeToV3 + "DROP INDEX messages_conversation; ALTER TABLE messages DROP COLUMN peer_key; ALTER TABLE messages DROP COLUMN is_live; DROP TABLE conversations; ALTER TABLE avatar_mappings DROP COLUMN disabled; PRAGMA user_version=2;";
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static CharacterIdentity Peer(ushort world = 7) => new() { Name = "Peer Name", HomeWorldId = world, HomeWorld = "PeerWorld" };
    public static Task DirectedTell() {
        var peer = Peer(); var target = TellTarget.From(peer);
        Check(target.IsValid && target.Format("hello /world") == "/tell Peer Name@PeerWorld hello /world", "Tell target formatting changed");
        foreach (var bad in new[] { target with { Name = "Peer Name\n/echo" }, target with { Name = "Peer Name@Other" }, target with { HomeWorld = "World /echo" }, target with { HomeWorldId = 0 } }) Check(!bad.IsValid, "Invalid target accepted");
        foreach (var text in new[] { "x\ny", "x\ry", "x\0y", " " }) {
            try { target.Format(text); throw new Exception("Injected body accepted"); } catch (ArgumentException) { }
        }
        var packet = new ClientMessage("text") { TellTarget = target, ExpectedOwnerKey = "cid:1", ExpectedOwnerEpoch = "login", RequestId = "test" };
        Check(ClientMessage.Decode(packet.Encode()[1..]).TellTarget == target, "Target wire fields changed");
        Check(ClientMessage.Decode(MessagePackSerializer.Serialize(new[] { "old message" })).TellTarget == null, "Old client acquired a target");
        var enriched = ConversationIdentity.Copy(peer); enriched.ContentId = 99;
        Check(ConversationIdentity.PeerKey(peer) == ConversationIdentity.PeerKey(enriched) && ConversationIdentity.PeerKey(peer) != ConversationIdentity.PeerKey(Peer(8)), "CID enrichment or same-name world split failed");
        var context = new GameCommandContext("cid:1", "login", 1);
        var command = new GameCommand(Guid.NewGuid(), "test", context, target.Format("text"), null, default, target);
        Check(GameCommandQueue.Validate(command, context with { ChannelRevision = 9 }) == null, "Current channel invalidated fixed target");
        Check(GameCommandQueue.Validate(command, context with { Epoch = "login2" }) == CommandFailure.IdentityChanged, "Relog retained queued Tell");
        var queue = new GameCommandQueue();
        Check(queue.TryEnqueue(new[] { command, command with { LastPart = false }, command with { RequestId = "other" } }), "Batch rejected");
        queue.CancelRequest(command.ClientId, "test");
        Check(queue.Usage.Count == 1 && queue.Peek()?.RequestId == "other", "Failed request retained trailing pieces or removed unrelated sends");
        return Task.CompletedTask;
    }
    public static async Task Storage() {
        var dir = Path.Combine(Path.GetTempPath(), "xivchat-phase3-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.db");
        try {
            await using (var db = await HistoryStore.OpenAsync(path)) {
                await db.AppendAsync("a", WorkbenchTests.Message(1), "a1"); await db.AnnotateAsync("a1", "迁移备注", true);
            }
            using (var sql = new SqliteConnection($"Data Source={path};Pooling=False")) { sql.Open(); using var cmd = sql.CreateCommand(); cmd.CommandText = DowngradeToV2; cmd.ExecuteNonQuery(); }
            await using (var db = await HistoryStore.OpenAsync(path)) {
                var migrated = (await db.GetConversationsAsync("a", "cid:1")).Single();
                Check(migrated.Unread == 0 && migrated.Latest is { Note: "迁移备注", Bookmarked: true }, "Backfill gained unread or lost annotations");
                var key = migrated.State.PeerKey;
                await db.SaveConversationAsync(migrated.State with { Draft = "我的草稿", Note = "会话备注", Pinned = true });
                var incoming = WorkbenchTests.Message(2); await db.AppendAsync("a", incoming, "a2", true);
                var anotherWorld = WorkbenchTests.Message(3); anotherWorld.TellPeer = Peer(8); await db.AppendAsync("a", anotherWorld, "a3", true);
                await db.AppendAsync("b", WorkbenchTests.Message(4), "b4", true);
                await db.AppendAsync("a", WorkbenchTests.Message(5, owner: 2), "a5", true);
                var same = (await db.GetConversationsAsync("a", "cid:1")).Single(c => c.State.PeerKey == key);
                Check(same.Unread == 1 && same.State is { Draft: "我的草稿", Note: "会话备注", Pinned: true }, "Conversation states were mixed");
                await db.MarkConversationReadAsync("a", "cid:1", key, same.Latest!.Message.Timestamp, same.Latest.RowId);
                Check((await db.GetConversationsAsync("a", "cid:1")).Single(c => c.State.PeerKey == key).Unread == 0, "Read marker not persisted");
                Check((await db.SearchAsync(new(Source: "a", OwnerKey: "cid:1", PeerKey: key))).Count == 2, "Peer search leaked");
                Check((await db.SearchAsync(new(Text: "迁移", BookmarksOnly: true))).Single().Id == "a1", "Note and bookmark search failed");
                var context = await db.GetContextAsync("a2");
                Check(context.Select(r => r.Id).Order().SequenceEqual(new[] { "a1", "a2", "a3" }), "Context crossed source / owner boundary");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { await db.GetContextAsync("a2", token: cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            }
            await using (var db = await HistoryStore.OpenAsync(path)) Check((await db.GetConversationsAsync("a", "cid:1")).Single(c => c.State.Pinned).State.Draft == "我的草稿", "Reopen lost draft");
            Check(Directory.GetFiles(dir, "*.before-v4-*.bak").Length == 1, "Migration backup missing");
        } finally { Directory.Delete(dir, true); }
    }
    private static string Entry(string id, string world = "PeerWorld") => $"<a class=\"entry__link\" href=\"/lodestone/character/{id}/\"><p class=\"entry__name\">Peer Name</p><p class=\"entry__world\">{world} [Light]</p></a>";
    private static string Page(string entries) => entries + "<li class=\"btn__pager__current\">1 of 1</li>";
    private static string Profile(string image = "https://img2.finalfantasyxiv.com/face.jpg") => $"<p class=\"frame__chara__name\">Peer Name</p><p class=\"frame__chara__world\">PeerWorld [Light]</p><div class=\"frame__chara__face\"><img src=\"{image}\"></div>";
    public static Task Parser() {
        Check(LodestoneParser.ExtractId("https://na.finalfantasyxiv.com/lodestone/character/22586842/") == "22586842", "Reference URL ID extraction failed");
        foreach (var bad in new[] { "https://example.com/lodestone/character/1/", "https://na.finalfantasyxiv.com.evil/character/1/", "https://user@na.finalfantasyxiv.com/lodestone/character/1/", "0", "https://na.finalfantasyxiv.com/lodestone/character/1/blog/" }) Check(LodestoneParser.ExtractId(bad) == null, "Unofficial URL accepted");
        Check(LodestoneParser.FindExact(Page(Entry("1") + Entry("2", "Other")), "Peer Name", "PeerWorld") == "1", "Cross-world exact match failed");
        Check(LodestoneParser.FindExact(Page(Entry("1") + Entry("2")), "Peer Name", "PeerWorld") == null, "Ambiguous match accepted");
        Check(LodestoneParser.FindExact(Page(Entry("1")).Replace("1 of 1", "1 of 2"), "Peer Name", "PeerWorld") == null, "Paginated match accepted");
        Check(LodestoneParser.FindExact("changed layout", "Peer Name", "PeerWorld") == null, "Unknown layout guessed");
        Check(LodestoneParser.ParseProfile(Profile(), "1").Avatar.Host == "img2.finalfantasyxiv.com", "Avatar extraction failed");
        try { LodestoneParser.ParseProfile(Profile("https://example.com/x"), "1"); throw new Exception("External image accepted"); } catch (FormatException) { }
        return Task.CompletedTask;
    }
    public static async Task Avatars() {
        var dir = Path.Combine(Path.GetTempPath(), "xivchat-avatars-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try {
            await using var db = await HistoryStore.OpenAsync(Path.Combine(dir, "history.db"));
            var handler = new Handler(); using var service = new LodestoneAvatars(() => db, Path.Combine(dir, "images"), true, handler);
            var initial = await service.GetAsync(Peer());
            Check(initial?.LodestoneId == "1" && service.ValidLocalPath(initial.ImagePath) && handler.Count == 3, "Auto lookup did not persist avatar");
            await service.GetAsync(Peer()); Check(handler.Count == 3, "Cached avatar caused network requests");
            service.SetEnabled(false); var manual = await service.BindAsync(Peer(), "22586842");
            Check(manual is { Manual: true, LodestoneId: "22586842" } && handler.Count == 3, "Offline manual override failed");
            var disabled = await service.BindAsync(Peer(), null); Check(disabled?.Disabled == true, "Unbind failed");
            service.SetEnabled(true); await service.GetAsync(Peer()); Check(handler.Count == 3, "Unbound identity queried automatically");
            handler.Fail = true; var failed = await service.BindAsync(Peer(), null, true);
            Check(failed?.RetryAt > DateTime.UtcNow, "Network failure not backed off");
            var count = handler.Count; await service.GetAsync(Peer()); Check(handler.Count == count, "Failure retried without backoff");
            handler.Fail = false; handler.Block = true;
            var old = service.BindAsync(Peer(), null, true);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.SetEnabled(false); var overwritten = await service.BindAsync(Peer(), "2");
            await old.WaitAsync(TimeSpan.FromSeconds(5));
            Check((await db.GetAvatarAsync(ConversationIdentity.PeerKey(Peer())!)) is { LodestoneId: "2", Manual: true }, "Cancelled auto lookup replaced manual binding");
        } finally { Directory.Delete(dir, true); }
    }
    private sealed class Handler : HttpMessageHandler {
        public int Count; public bool Fail, Block; public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Count++;
            if (Block) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
            if (Fail) throw new HttpRequestException("offline");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "img2.finalfantasyxiv.com"
                ? new ByteArrayContent(new byte[] { 255, 216, 255, 224, 0, 0, 0, 0 })
                : new StringContent(request.RequestUri.Query.Length > 0 ? Page(Entry("1")) : Profile()) };
        }
    }
}
