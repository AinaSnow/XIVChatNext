using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Sodium;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    internal sealed partial class DesktopCardsSmokeApp {
        private readonly string cardTestDirectory = Path.Combine(Path.GetTempPath(), "xfw-card-ui-" + Guid.NewGuid().ToString("N"));
        private sealed class ChineseFixture : HttpMessageHandler {
            internal string Suffix = "";
            internal int FullRequests, NameRequests;
            internal TaskCompletionSource? PendingText;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
                var rows = request.RequestUri!.Query.TrimStart('?').Split('&').FirstOrDefault(q => q.StartsWith("rows="));
                object Row(uint id) => new { row_id = id, fields = new { Name = (id == 123 ? "中文测试戒指" : "中文资料 " + id) + Suffix, Singular = "中文 NPC " + id,
                    Description = "中文说明：数值来自游戏文件。", BaseParam = new[] { new { row_id = 1, fields = new { Name = "力量" } } } } };
                object body;
                if (rows != null) { NameRequests++; body = new { version = "chinese-fixture-v2", schema = "fixture", rows = Uri.UnescapeDataString(rows[5..]).Split(',').Select(uint.Parse).Select(Row).ToArray() }; }
                else {
                    FullRequests++;
                    if (PendingText != null) await PendingText.Task.WaitAsync(token);
                    var id = uint.Parse(request.RequestUri.Segments.Last());
                    body = new { version = "chinese-fixture-v2", schema = "fixture", row_id = id, fields = new { Name = (id == 123 ? "中文测试戒指" : "中文资料 " + id) + Suffix,
                        Description = "中文说明：数值来自游戏文件。", BaseParam = new[] { new { row_id = 1, fields = new { Name = "力量" } }, new { row_id = 21, fields = new { Name = "物理防御" } }, new { row_id = 24, fields = new { Name = "魔法防御" } } } } };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            }
        }
        private readonly ChineseFixture chineseFixture = new();
        private void ConfigureChineseFixture() => typeof(App).GetField("cards", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this,
            new GameCardSession(this, new ChineseGameText(Path.Combine(cardTestDirectory, "chinese"), new HttpClient(chineseFixture))));
        private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)((FrameworkElement)window.Content).FindName(name);
        private static void Click(Window window, string name) => ((IInvokeProvider)new ButtonAutomationPeer(Control<Button>(window, name)).GetPattern(PatternInterface.Invoke)).Invoke();
        private static async Task Until(Func<bool> condition) { for (int i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(50); } throw new Exception("Native card condition timed out"); }
        private static TextChunk Gear(uint id, int value = 100) => new("Fixture ring") { ItemId = id, ItemKind = 1_000_000,
            ItemName = "Fixture ring " + id, ItemCategory = "Ring", ItemEquipLevel = 90, ItemLevel = 700,
            ItemDetails = new() { EquipSlotCategoryId = 12, EquipSlots = new[] { 11, 12 }, AllowedJobs = new uint[] { 19 }, ClassJobs = "PLD", Parameters = new() { new() { Id = 1, Name = "Strength", NqValue = value, HqDelta = 5 } } },
            DataSource = new() { Language = "English", Version = "workflow-game-v2", RetrievedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } };
        private async Task TestWorkflow(ItemWindow item) {
            LocalizationHelper.ApplyLanguage(AppLanguage.English);
            var store = await HistoryStore.OpenAsync(Path.Combine(cardTestDirectory, "history.db")); Session.Store = store;
            var window = new MainWindow(); typeof(App).GetProperty(nameof(Window))!.SetValue(this, window); window.Activate();
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var key = PublicKeyBox.GenerateKeyPair(); Config.TrustedKeys.Add(new TrustedKey("Cards fixture", key.PublicKey));
            var accept = listener.AcceptTcpClientAsync(); Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer = await accept; var stream = peer.GetStream(); await stream.ReadExactlyAsync(new byte[3]);
            var handshake = await KeyExchange.ServerHandshake(key, stream);
            var preferences = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
            Check(ClientPreferences.Decode(preferences[1..]).TryGetValue<bool>(ClientPreference.GameCardsSupport, out var supported) && supported, "Client negotiates appended game-card capability");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var sendGate = new SemaphoreSlim(1);
            async Task Send(Encodable packet) { await sendGate.WaitAsync(timeout.Token); try { await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, packet); } finally { sendGate.Release(); } }
            var owner = new CharacterIdentity { ContentId = 1, Name = "Card Tester", HomeWorldId = 1, HomeWorld = "Home" };
            var snapshot = new EquipmentSnapshot { OwnerKey = owner.Key!, OwnerEpoch = "login1", ClassJobId = 19, Level = 100, Revision = 1, IsLive = true,
                CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Items = new[] {
                    new CardEquippedItem { Slot = 11, Item = Gear(456, 90), Materia = new[] { new CardMateria { Item = new() { Id = 777, Name = "Actual materia" }, ParameterId = 1, ParameterName = "Strength", Value = 36 } } },
                    new CardEquippedItem { Slot = 12, Item = Gear(457, 80) } } };
            var queries = new List<ClientGameCard>();
            var delayed = new List<Task>();
            async Task SendLate(ServerGameCard response) { try { await Task.Delay(500, timeout.Token); await Send(response); } catch (OperationCanceledException) { } }
            async Task Serve() {
                try {
                    while (!timeout.IsCancellationRequested) {
                        var bytes = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                        if (bytes[0] != (byte)ClientOperation.GameCard) continue;
                        var request = ClientGameCard.Decode(bytes[1..]); queries.Add(request);
                        var response = new ServerGameCard { RequestId = request.RequestId, Query = request.Query, Id = request.Id, ItemKind = request.ItemKind,
                            DataScope = "fixture-scope", Source = Gear(123).DataSource, Page = request.Page };
                        switch (request.Query) {
                            case CardQuery.Equipment: response.Equipment = snapshot; break;
                            case CardQuery.Item: response.Item = Gear(request.Id); response.Item.ItemKind = request.ItemKind; break;
                            case CardQuery.Map: response.Map = new() { Id = request.Id, Name = "Confirmed map", SizeFactor = 200 }; break;
                            case CardQuery.Recipes:
                                response.PageCount = 2; response.Recipes = new[] { new CardRecipe { Id = (uint)(1 + request.Page), CraftJob = "Goldsmith", Level = 90, Yield = 1,
                                    Ingredients = new[] { new CardIngredient { Item = new() { Id = (uint)(234 + request.Page), Name = "Material page " + request.Page }, Quantity = 3 } } } }; break;
                            case CardQuery.Sources:
                                response.Sources = new[] { new CardItemSource { Kind = ItemSourceKind.GilShop, Id = 1, Name = "Fixture shop", NpcId = 12, NpcName = "Confirmed vendor", GilPrice = 42,
                                    Location = new() { Id = 55, Name = "Confirmed map", X = 10, Y = 20, SizeFactor = 200 } } }; break;
                        }
                        Check(response.Valid, "Fixture response passes production validation: " + request.Query);
                        if (request.Query == CardQuery.Item && request.Id == 901) { delayed.Add(SendLate(response)); continue; }
                        await Send(response);
                    }
                } catch (OperationCanceledException) { } catch (EndOfStreamException) { } catch (IOException) when (timeout.IsCancellationRequested) { }
            }
            var server = Serve();
            try {
                await Send(new ServerCapabilities { ServiceId = "cards", RunId = "run", GameCards = true });
                await Send(new PlayerData("Home", "Home", "Map", owner.Name) { Identity = owner, OwnerEpoch = "login1" }); await Send(new Availability(true));
                await Until(() => Connection?.Available == true && Session.Player?.OwnerEpoch == "login1");
                var originMessage = new ServerMessage(DateTime.UtcNow, ChatType.Say, Encoding.UTF8.GetBytes("Tester"), Encoding.UTF8.GetBytes("The original ring message"), new List<Chunk> { new TextChunk("The original ring message"), Gear(123) }) { Owner = owner };
                await Session.RecordAsync(originMessage, Connection!.Source, true);
                var origin = Cards.Origin(originMessage); var web = Web(item, "ItemWebView");
                item.UpdateItem(123, true, "Ring", Gear(123), origin); item.Activate();
                await Wait(web, "document.body.innerText.includes('Material page 0') && document.body.innerText.includes('Actual materia') && document.querySelector('.positive') !== null");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.positive').innerText === '+10'") == "true", "Current gear and actual materia render with base delta only");
                await web.CoreWebView2.ExecuteScriptAsync("send('compare:12')"); await Wait(web, "document.querySelector('.positive')?.innerText === '+20'"); Check(true, "Left ring selection updates independent comparison");
                await web.CoreWebView2.ExecuteScriptAsync("send('recipes:1')"); await Wait(web, "document.body.innerText.includes('Material page 1')");
                Check(queries.Any(q => q.Query == CardQuery.Recipes && q.Page == 1 && q.DataScope == "fixture-scope"), "Next recipe page carries the original data scope");
                await web.CoreWebView2.ExecuteScriptAsync("send('item:235')"); await Wait(web, "document.querySelector('h1').innerText.includes('235')");
                Check(Control<Button>(item, "CardBack").Visibility == Visibility.Visible, "Material card retains a back path");
                Click(item, "CardBack"); await Wait(web, "document.querySelector('h1').innerText.includes('123') && document.body.innerText.includes('Material page 0')");
                Check(true, "Back restores original item card");
                item.UpdateItem(901, true, "First", Gear(901), origin);
                await Until(() => queries.Any(q => q.Query == CardQuery.Item && q.Id == 901));
                item.UpdateItem(902, true, "Second", Gear(902), origin);
                await Wait(web, "document.querySelector('h1').innerText.includes('902')"); await Task.WhenAll(delayed); await Task.Delay(100);
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('h1').innerText.includes('902')") == "true", "Late item reply cannot overwrite a newer card");
                item.UpdateItem(123, true, "Ring", Gear(123), origin); await Wait(web, "document.querySelector('h1').innerText.includes('123') && document.body.innerText.includes('Confirmed vendor')");
                await web.CoreWebView2.ExecuteScriptAsync("send('map:0')");
                MapWindow mapWindow = null!;
                await Until(() => (mapWindow = (MapWindow)typeof(MapWindow).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!) != null);
                await Wait(Web(mapWindow, "MapWebView"), "document.querySelector('a')?.href.includes('id=55') === true");
                var mapSnapshot = (TextChunk)typeof(MapWindow).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mapWindow)!;
                Check(mapSnapshot.MapId == 55 && mapSnapshot.MapX == 10 && mapSnapshot.MapY == 20 && mapSnapshot.MapSizeFactor == 200, "Source map preserves confirmed coordinates and loads its own metadata"); mapWindow.Close(); item.Activate();
                Check(GameCardHtml.Copy(new ItemCardState { Item = Gear(123) }).Contains("Strength 105"), "Copy text uses the displayed HQ totals");
                Click(item, "CardFavorite"); await Until(() => Control<TextBlock>(item, "CardStatusText").Text.Contains("Saved"));
                window.Navigate("favorites"); await Until(() => Control<ListView>(window, "FavoritesList").Items.Count == 1);
                var favorite = (CardFavorite)Control<ListView>(window, "FavoritesList").Items[0];
                Check((await store.GetCardSourcesAsync(favorite.Id, origin.Source, origin.OwnerKey)).Single().Id == originMessage.LocalStorageId, "Favorite links the stable legacy message ID");
                await store.UpdateCardFavoriteMetadataAsync(favorite.Id, favorite.Source, favorite.OwnerKey, "Raid", "tank", "Keep this note");
                Control<TextBox>(window, "FavoriteSearch").Text = "Keep"; Control<TextBox>(window, "FavoriteGroup").Text = "Raid"; Control<TextBox>(window, "FavoriteTag").Text = "tank";
                Click(window, "FavoriteApply"); await Until(() => Control<ListView>(window, "FavoritesList").Items.OfType<CardFavorite>().Any(c => c.Note == "Keep this note"));
                Check(((CardFavorite)Control<ListView>(window, "FavoritesList").Items[0]).Note == "Keep this note", "Favorites filter names/notes, groups and tags without rewriting snapshots");
                Control<TextBox>(window, "FavoriteSearch").Text = ""; Control<TextBox>(window, "FavoriteGroup").Text = ""; Control<TextBox>(window, "FavoriteTag").Text = "";
                Click(item, "CardSources"); await Until(() => Control<TextBlock>(window, "ChatSubtitle").Text.Contains("The original ring message"));
                Check(Control<FrameworkElement>(window, "ComposerPanel").Visibility == Visibility.Collapsed, "Source action opens read-only message context");
                LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified); await Wait(web, "document.querySelector('h1').innerText.includes('中文测试戒指') && document.body.innerText.includes('力量 +105')");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.body.textContent.includes('chinese-fixture-v2') && document.body.textContent.includes('workflow-game-v2') && !document.querySelector('#card-provenance').open") == "true", "Chinese and game provenance remain available in a collapsed details section");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.body.innerText.includes('Ring（暂无中文）') && document.body.innerText.includes('Fixture shop') === false") == "true", "Untranslated fields are marked while translated source names replace game originals");
                var shownState = (ItemCardState)typeof(ItemWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!;
                Check(GameCardHtml.Copy(shownState).StartsWith("中文测试戒指 HQ") && GameCardHtml.Copy(shownState).Contains("力量 105"), "Copied names and attributes use the same Chinese text and game values as the card");
                var fullRequests = chineseFixture.FullRequests; var nameRequests = chineseFixture.NameRequests;
                chineseFixture.Suffix = " · 已更新";
                Click(item, "CardRefresh");
                await Wait(web, "document.querySelector('h1').innerText.includes('已更新') && document.body.innerText.includes('中文资料 234 · 已更新')");
                Check(chineseFixture.FullRequests > fullRequests && chineseFixture.NameRequests > nameRequests, "Refresh bypasses both full-item and linked-name caches");
                Click(item, "CardFavorite"); await Until(() => Control<TextBlock>(item, "CardStatusText").Text.Contains("已收藏"));
                var refreshedFavorite = (await store.GetCardFavoritesAsync(origin.Source, origin.OwnerKey)).Single();
                Check(refreshedFavorite.Name == "中文测试戒指 · 已更新" && refreshedFavorite.Note == "Keep this note", "Favorite stores the displayed translated name and preserves user notes");
                await web.CoreWebView2.ExecuteScriptAsync("window.cardDocumentMarker=42;document.querySelector('#card-provenance').open=true;window.scrollTo(0,120)");
                var scrollBefore = await web.CoreWebView2.ExecuteScriptAsync("window.scrollY");
                await web.CoreWebView2.ExecuteScriptAsync("send('compare:12')");
                await Wait(web, "document.querySelector('.positive')?.innerText === '+20'");
                Check(await web.CoreWebView2.ExecuteScriptAsync("window.cardDocumentMarker===42 && document.querySelector('#card-provenance').open && Math.abs(window.scrollY-" + scrollBefore + ")<2") == "true", "Card updates retain the document, reading position and expanded details");
                var original = Control<CheckBox>(item, "CardOriginal"); original.IsChecked = true;
                typeof(ItemWindow).GetMethod("Original_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(item, new object[] { original, new RoutedEventArgs() });
                await Wait(web, "document.querySelector('h1').innerText.includes('Fixture ring 123') && document.body.innerText.includes('Strength +105')"); Check(true, "Original-language switch preserves numeric values");
                using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "cards-workflow-preview.png"))) await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture.AsRandomAccessStream());
                await Send(new Availability(false)); await Until(() => Connection?.Available == false); await Wait(web, "document.body.innerText.includes('非实时')");
                Check((await Cards.EquipmentAsync(origin))?.IsLive == false, "Logout marks the displayed equipment snapshot stale");
                await Send(new ServerGameCard { Query = CardQuery.Equipment, Equipment = snapshot }); await Task.Delay(100);
                Check((await Cards.EquipmentAsync(origin))?.IsLive == false, "Late equipment cannot become live while logged out");
                var other = new CharacterIdentity { ContentId = 2, Name = "Other Tester", HomeWorldId = 1, HomeWorld = "Home" };
                await Send(new PlayerData("Home", "Home", "Map", other.Name) { Identity = other, OwnerEpoch = "login2" });
                await Until(() => Session.Player?.OwnerEpoch == "login2");
                await Send(new ServerGameCard { Query = CardQuery.Equipment, Equipment = snapshot }); await Task.Delay(150);
                Check(await Cards.EquipmentAsync(new(origin.Source, other.Key!)) == null && (await Cards.EquipmentAsync(origin))?.IsLive == false, "Late equipment reply cannot attach to another character or reactivate stale gear");
                window.Navigate("favorites"); await Until(() => Control<TextBlock>(window, "FavoritesStatus").Text.Contains("还没有")); Check(Control<ListView>(window, "FavoritesList").Items.Count == 0, "Other character has an independent favorites list");
                Control<ComboBox>(window, "FavoriteScope").SelectedIndex = 1; await Until(() => Control<ListView>(window, "FavoritesList").Items.Count == 1);
                Check(((CardFavorite)Control<ListView>(window, "FavoritesList").Items[0]).OwnerKey == origin.OwnerKey, "All-characters view retains original favorite ownership");
                var manyLinks = new ServerMessage(DateTime.UtcNow.AddSeconds(1), ChatType.Say, Encoding.UTF8.GetBytes("Tester"), Encoding.UTF8.GetBytes("batch links"),
                    Enumerable.Range(1000, 250).Select(id => (Chunk)Gear((uint)id)).ToList()) { Owner = owner };
                await store.AppendAsync(origin.Source, manyLinks, "many-links");
                Control<ComboBox>(window, "FavoriteMode").SelectedIndex = 1; await Until(() => Control<TextBlock>(window, "FavoritesStatus").Text.Contains("最近消息"));
                Check(Control<ListView>(window, "FavoritesList").Items.Count == 100 && Control<Button>(window, "FavoritesMore").IsEnabled, "Recent appearances bound large multi-link messages to 100 cards per page");
                Click(window, "FavoritesMore"); await Until(() => Control<ListView>(window, "FavoritesList").Items.Count == 200);
                Click(window, "FavoritesMore"); await Until(() => Control<ListView>(window, "FavoritesList").Items.Count == 251);
                Check(Control<ListView>(window, "FavoritesList").Items.OfType<CardFavorite>().Select(c => c.Id).Distinct().Count() == 251 &&
                    Control<ListView>(window, "FavoritesList").Items.OfType<CardFavorite>().Any(c => c.Snapshot.ItemId == 123), "Recent pagination resumes within a message without losing or duplicating links");
                var unassigned = new ServerMessage(DateTime.UtcNow, ChatType.Say, Array.Empty<byte>(), Array.Empty<byte>(), new());
                Check(Cards.Origin(unassigned).OwnerKey.StartsWith("unassigned:"), "Unassigned old message is not attributed to the active character");
                Disconnect(); await Until(() => Connection == null);
                var offline = await Cards.LoadAsync(origin, CardQuery.Recipes, 123, 1_000_000, 1, "fixture-scope", Gear(123).DataSource, default);
                Check(offline.Cached && offline.Page?.Recipes[0].Ingredients[0].Item.Id == 235, "Previously visited recipe page remains available offline");
                item.UpdateItem(123, true, "Ring", Gear(123), origin); await Wait(web, "document.querySelector('h1').innerText.includes('123') && document.body.innerText.includes('非实时')");
                Check(true, "Disconnected card renders stale equipment explicitly");
            } finally {
                timeout.Cancel(); peer.Close(); Disconnect(); await server;
                await Notifier.FlushAsync(); await Workbench.FlushAsync(); Session.Store = null; await store.DisposeAsync();
                typeof(MainWindow).GetMethod("DisposeViews", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); window.Close();
            }
        }
    }
}
