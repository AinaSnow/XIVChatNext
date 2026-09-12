using System.Net;
using System.Text;
using MessagePack;
using Microsoft.Data.Sqlite;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatPlugin;
using XIVChatStorage;
using XIVChat_Desktop;

internal static class Phase5Tests {
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static TextChunk Item(uint id = 123) => new("Test item") {
        ItemId = id, ItemKind = 1_000_000, ItemName = "Test item", ItemEquipLevel = 90,
        ItemDetails = new() { EquipSlots = new[] { 11, 12 }, AllowedJobs = new uint[] { 19 },
            Parameters = new() { new() { Id = 1, Name = "Strength", NqValue = 100, HqDelta = 5 } } },
    };
    private static EquipmentSnapshot Equipment() => new() { OwnerKey = "cid:1", OwnerEpoch = "login", IsLive = true, ClassJobId = 19, Level = 100,
        CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Revision = 1,
        Items = new[] { new CardEquippedItem { Slot = 11, Item = Item(456) }, new CardEquippedItem { Slot = 12, Item = Item(457) } } };
    private static ServerGameCard Page(string scope = "scope", string version = "v1") => new() { RequestId = "test", Query = CardQuery.Item, Id = 123, ItemKind = 1_000_000,
        Item = Item(), DataScope = scope, Source = new() { Language = "English", Version = version, RetrievedAtUnixMilliseconds = 42 } };
    public static Task ProtocolAndQueue() {
        var request = new ClientGameCard { RequestId = "test", Query = CardQuery.Recipes, Id = 123 };
        Check(request.Valid && ClientGameCard.Decode(request.Encode()[1..]).Id == 123, "Valid request failed wire round trip");
        request.Page = 1; Check(!request.Valid, "Continuation accepted without data scope"); request.DataScope = "scope"; Check(request.Valid, "Scoped page rejected");
        request.Page = 256; Check(!request.Valid, "Page exceeded bound"); request.Page = 0;
        var page = Page(); Check(page.Valid && ServerGameCard.Decode(page.Encode()[1..]).Valid, "Item reply failed wire round trip");
        page.Item!.ItemId = 456; Check(!page.Valid, "Mismatched item payload accepted"); page = Page();
        page.Recipes = null!; Check(!page.Valid, "Null page array accepted"); page = Page();
        page.Item!.ItemDetails!.Parameters = null!; Check(!page.Valid, "Null stats accepted");
        var equipment = Equipment(); equipment.Items[0].Materia = new CardMateria[] { null! }; Check(!CardProtocol.ValidEquipment(equipment), "Null materia accepted");
        var queue = new GameCardRequestQueue(); var client = Guid.NewGuid();
        for (int i = 0; i < 4; i++) Check(queue.Enqueue(new(client, request, default, DateTime.UtcNow)), "Per-client capacity too small");
        var active = queue.Take()!; Check(!queue.Enqueue(new(client, request, default, DateTime.UtcNow)), "Dequeued work escaped per-client budget");
        queue.Complete(active); Check(queue.Enqueue(new(client, request, default, DateTime.UtcNow)), "Completed work did not free budget"); queue.Clear();
        for (int i = 0; i < 32; i++) Check(queue.Enqueue(new(Guid.NewGuid(), request, default, DateTime.UtcNow)), "Global queue capacity too small");
        Check(!queue.Enqueue(new(Guid.NewGuid(), request, default, DateTime.UtcNow)), "Global bound exceeded");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); queue.Clear();
        Check(!queue.Enqueue(new(client, request, cancelled.Token, DateTime.UtcNow)), "Cancelled request entered queue");
        var oldCaps = MessagePackSerializer.Serialize(new object[] { "service", "run", false, false, false, false, false, false, false, false });
        Check(!ServerCapabilities.Decode(oldCaps).GameCards, "Old server incorrectly advertises game cards");
        return Task.CompletedTask;
    }
    public static Task Comparison() {
        var item = Item(); var snapshot = Equipment(); snapshot.Items[0].Item.ItemDetails!.Parameters[0].NqValue = 90;
        snapshot.Items[1].Item.ItemDetails!.Parameters[0].NqValue = 80;
        snapshot.Items[0].Materia = new[] { new CardMateria { Item = new() { Id = 500, Name = "Materia" }, Value = 36 } };
        Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Parameters.Single().Delta == 10 &&
            GearComparer.Compare(item, snapshot, snapshot.Items[1]).Parameters.Single().Delta == 20, "Ring slots or HQ totals merged incorrectly");
        item.ItemKind = 0; Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Parameters.Single().Delta == 5, "Materia leaked into base comparison");
        snapshot.ClassJobId = 1; Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Reason == GearComparisonReason.WrongJob, "Job restriction ignored"); snapshot.ClassJobId = 19;
        snapshot.Level = 80; Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Reason == GearComparisonReason.LevelTooLow, "Level restriction ignored"); snapshot.Level = 100;
        snapshot.Items[0].HasCustomStats = true; Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Reason == GearComparisonReason.SpecialEquipment, "Custom equipment scored"); snapshot.Items[0].HasCustomStats = false;
        snapshot.Items[0].Slot = 0; item.ItemDetails!.EquipSlots = new[] { 0 };
        Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Reason == GearComparisonReason.Weapon, "Weapon scored as armor");
        item.ItemDetails.EquipSlots = new[] { 3 }; Check(GearComparer.Compare(item, snapshot, snapshot.Items[0]).Reason == GearComparisonReason.WrongSlot, "Different slot scored");
        return Task.CompletedTask;
    }
    public static async Task Storage() {
        var directory = Path.Combine(Path.GetTempPath(), "xfw-cards-" + Guid.NewGuid()); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "history.db");
        try {
            await using (var original = await HistoryStore.OpenAsync(path)) { await original.AppendAsync("source", WorkbenchTests.Message(1), "origin"); await original.AnnotateAsync("origin", "keep note", true); }
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False")) {
                db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE card_cache; DROP TABLE card_equipment; DROP TABLE card_favorites; PRAGMA user_version=4"; command.ExecuteNonQuery();
            }
            await using var store = await HistoryStore.OpenAsync(path);
            Check((await store.GetMessageAsync("origin")) is { Note: "keep note", Bookmarked: true }, "V4 migration lost history annotations");
            Check(Directory.GetFiles(directory, "*.before-v5-*.bak").Length == 1, "V5 migration backup missing");
            await store.SaveCardPageAsync("source", Page());
            Check(await store.GetCardPageAsync("other", CardQuery.Item, 123, 1_000_000) == null &&
                await store.GetCardPageAsync("source", CardQuery.Item, 123, 1_000_000, version: "v2") == null &&
                await store.GetCardPageAsync("source", CardQuery.Item, 123, 1_000_000, dataScope: "other") == null, "Cache source/version/scope crossed");
            Check((await store.GetCardPageAsync("source", CardQuery.Item, 123, 1_000_000))?.Item?.ItemName == "Test item", "Static snapshot missing");
            var snapshot = Equipment(); await store.SaveEquipmentAsync("source", snapshot);
            Check(snapshot.IsLive && (await store.GetEquipmentAsync("source", "cid:1"))?.IsLive == false && await store.GetEquipmentAsync("other", "cid:1") == null, "Persisted equipment claimed live or crossed server");
            var favorite = new CardFavorite { Id = "favorite", Source = "source", OwnerKey = "cid:1", Name = "Test item", Snapshot = Item(), SavedAt = 100 };
            await store.SaveCardFavoriteAsync(favorite, "origin");
            using var exportedSource = new StringWriter();
            Check(await store.ExportAsync(new(Source: "source", OwnerKey: "cid:1", FavoriteId: "favorite"), exportedSource, false, true) == 1,
                "Favorite source export lost its scoped message");
            using var wrongOwnerExport = new StringWriter();
            Check(await store.ExportAsync(new(Source: "source", OwnerKey: "cid:2", FavoriteId: "favorite"), wrongOwnerExport, false, true) == 0,
                "Favorite source export crossed owners");
            await store.UpdateCardFavoriteMetadataAsync(favorite.Id, favorite.Source, favorite.OwnerKey, "装备", "坦克 毕业", "保留私人备注");
            await store.SaveCardFavoriteAsync(favorite, "origin");
            Check((await store.GetCardFavoritesAsync(null, null, search: "私人", group: "装备", tag: "坦克")).Single().Note == "保留私人备注", "Refreshing a favorite lost metadata or cross-owner search failed");
            await store.UpdateCardFavoriteMetadataAsync(favorite.Id, "wrong", favorite.OwnerKey, "", "", "erased");
            Check((await store.GetCardFavoritesAsync("source", "cid:1")).Single().Group == "装备", "Foreign metadata update succeeded");
            await store.AppendAsync("other", WorkbenchTests.Message(2), "other-origin"); await store.SaveCardFavoriteAsync(favorite, "other-origin");
            Check((await store.GetCardSourcesAsync("favorite", "source", "cid:1")).Count == 1 &&
                (await store.GetCardFavoritesAsync("source", "cid:2")).Count == 0, "Favorite sources crossed source/owner");
            favorite.Source = "other";
            try { await store.SaveCardFavoriteAsync(favorite); throw new Exception("Cross-source overwrite accepted"); } catch (ArgumentException) { }
            Check((await store.GetCardFavoritesAsync("source", "cid:1")).Single().Source == "source", "Rejected overwrite mutated saved favorite");
            await store.DeleteCardFavoriteAsync("favorite", "source", "cid:2"); Check((await store.GetCardFavoritesAsync("source", "cid:1")).Count == 1, "Foreign delete succeeded");
            await store.PruneAsync(1, DateTime.UtcNow.AddYears(2)); Check(await store.GetMessageAsync("origin") != null, "Retention removed favorite source");
            await store.DeleteCardFavoriteAsync("favorite", "source", "cid:1"); Check((await store.GetCardFavoritesAsync("source", "cid:1")).Count == 0 && await store.GetMessageAsync("origin") != null, "Deleting favorite deleted chat");
        } finally { Directory.Delete(directory, true); }
    }
    private sealed class Handler : HttpMessageHandler {
        internal int Calls;
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => throw new HttpRequestException("offline");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Reply(request, token); }
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    public static async Task ChineseText() {
        var directory = Path.Combine(Path.GetTempPath(), "xfw-chinese-" + Guid.NewGuid());
        using var handler = new Handler(); using var http = new HttpClient(handler); var provider = new ChineseGameText(directory, http);
        try {
            handler.Reply = (request, _) => {
                Check(request.RequestUri!.Host == "xivapi-v2.xivcdn.com" && request.RequestUri.Query.Contains("language=chs"), "Wrong Chinese API or language");
                Check(Uri.UnescapeDataString(request.RequestUri.Query).Contains("BaseParam[].Name"), "Array field selection incorrect");
                return Task.FromResult(Json("""{"row_id":123,"version":"latest-v7","schema":"schema-v2","fields":{"Name":"测试戒指","Description":"中文说明","BaseParam":[{"row_id":1,"fields":{"Name":"力量"}}],"DamagePhys":999999}}"""));
            };
            var text = await provider.ItemAsync(123, false, false, default);
            Check(text?.Name == "测试戒指" && text.Parameters[1] == "力量" && text.Version == "latest-v7", "Chinese labels or actual server version missing");
            await provider.ItemAsync(123, false, false, default); Check(handler.Calls == 1, "Fresh Chinese cache fetched again");
            handler.Reply = (_, _) => throw new HttpRequestException("offline");
            Check((await provider.ItemAsync(123, false, true, default))?.Name == "测试戒指", "Offline translation cache lost");
            handler.Reply = (_, _) => Task.FromResult(Json(new string('x', 256_001)));
            Check((await provider.ItemAsync(123, false, true, default))?.Name == "测试戒指", "Oversized response destroyed cache");
            handler.Reply = async (_, token) => { await Task.Delay(10_000, token); return Json("{}"); };
            using var cancel = new CancellationTokenSource(30);
            try { await provider.ItemAsync(123, false, true, cancel.Token); throw new Exception("Caller cancellation swallowed"); } catch (OperationCanceledException) { }
            handler.Reply = (request, _) => {
                Check(request.RequestUri!.Query.Contains("rows=4,5") && request.RequestUri.Query.Contains("language=chs"), "Batch names requested incorrectly");
                return Task.FromResult(Json("""{"version":"current","schema":"s","rows":[{"row_id":4,"fields":{"Name":"铜矿"}},{"row_id":5,"fields":{"Name":"铁矿"}},{"row_id":6,"fields":{"Name":"unrequested"}}]}"""));
            };
            var names = await provider.NamesAsync("Item", new uint[] { 4, 5, 4 }, default);
            Check(names.Count == 2 && names[4] == "铜矿", "Batch labels missing or unrequested IDs accepted");
            var calls = handler.Calls;
            await provider.NamesAsync("Item", new uint[] { 4, 5 }, default);
            Check(handler.Calls == calls, "Fresh names unnecessarily requested again");
            handler.Reply = (_, _) => Task.FromResult(Json("""{"version":"new","schema":"s","rows":[{"row_id":4,"fields":{"Name":"更新铜矿"}},{"row_id":5,"fields":{"Name":"更新铁矿"}}]}"""));
            var refreshedNames = await provider.NamesAsync("Item", new uint[] { 4, 5 }, default, refresh: true);
            Check(handler.Calls == calls + 1 && refreshedNames[4] == "更新铜矿", "Explicit refresh reused fresh name cache");
            handler.Reply = (_, _) => throw new HttpRequestException("offline");
            Check((await provider.NamesAsync("Item", new uint[] { 4, 5 }, default, refresh: true))[4] == "更新铜矿", "Failed name refresh destroyed readable cached text");
            handler.Reply = (_, _) => Task.FromResult(Json("""{"row_id":55,"version":"map-v2","schema":"s","fields":{"PlaceName":{"fields":{"Name":"中文地图"}},"SizeFactor":99999}}"""));
            var map = await provider.MapAsync(55, false, default); Check(map?.Name == "中文地图" && map.Version == "map-v2", "Map translation lost its text or provenance");
            try { await provider.NamesAsync("../anything", new uint[] { 1 }, default); throw new Exception("Unapproved table accepted"); } catch (ArgumentException) { }
            var item = Item(); Check(item.ItemDetails!.Parameters[0].Value(true) == 105, "Remote numeric fields affected game values");
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
