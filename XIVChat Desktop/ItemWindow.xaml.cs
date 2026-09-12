using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public sealed partial class ItemWindow : Window {
        private static ItemWindow? _instance;
        private App App => (App)Application.Current;
        private ItemCardState state = new();
        private readonly Stack<ItemCardState> parents = new();
        private CancellationTokenSource cancellation = new();
        private int renderVersion;
        private bool webReady;
        private bool closed;
        private ItemCardState? renderedState;
        private double restoreScroll;
        private string documentUri = "about:blank";
        public ItemWindow() {
            InitializeComponent(); Branding.ApplyWindowIcon(this);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(850, 900));
            Localize.BindWindow(this, LocalizeCard);
            App.Cards.EquipmentChanged += EquipmentChanged;
            Closed += (_, _) => { closed = true; cancellation.Cancel(); cancellation.Dispose(); renderVersion++; App.Cards.EquipmentChanged -= EquipmentChanged; _instance = null; };
        }
        private void LocalizeCard() {
            CardBack.Content = L("Card.Back"); CardCopy.Content = L("Card.Copy"); CardFavorite.Content = L("Card.Favorite");
            CardSources.Content = L("Card.MessageSources"); CardRefresh.Content = L("Card.Refresh"); CardOriginal.Content = L("Card.Original");
            state.ShowChinese = LocalizationHelper.LanguageCode.StartsWith("zh") && CardOriginal.IsChecked != true;
            _ = RenderAsync();
            if (state.ShowChinese && state.Item.ItemId > 0) _ = TranslateSafelyAsync(state, cancellation.Token);
        }
        private static string L(string key) => LocalizationHelper.GetString(key);
        public static void ShowItem(uint? itemId, bool isHq, string? itemName, TextChunk? chunk = null, CardOrigin? origin = null) {
            _instance ??= new ItemWindow(); _instance.Activate(); _instance.UpdateItem(itemId, isHq, itemName, chunk, origin);
        }
        public void UpdateItem(uint? itemId, bool isHq, string? itemName, TextChunk? chunk = null, CardOrigin? origin = null) {
            parents.Clear(); Open(itemId, isHq, itemName, chunk, origin ?? App.Cards.Origin());
        }
        private void Open(uint? id, bool hq, string? name, TextChunk? chunk, CardOrigin origin) {
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
            var identity = GameItemIdentity.Resolve(id ?? 0, chunk?.ItemKind, hq);
            var copy = chunk == null ? new TextChunk(name ?? "") : MessagePack.MessagePackSerializer.Deserialize<TextChunk>(MessagePack.MessagePackSerializer.Serialize(chunk));
            copy.ItemId = identity.Id; copy.ItemKind = (uint)identity.Kind; copy.ItemName ??= name ?? "#" + identity.Id;
            state = new ItemCardState { Item = copy, Origin = origin, ShowChinese = LocalizationHelper.LanguageCode.StartsWith("zh") && CardOriginal.IsChecked != true };
            CardBack.Visibility = parents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CardStatusText.Text = L("Card.Loading"); _ = RenderAsync(); _ = LoadAsync(state, cancellation.Token);
        }
        private bool IsCurrent(ItemCardState target, CancellationToken token) => !closed && !token.IsCancellationRequested && ReferenceEquals(state, target);
        private async Task LoadAsync(ItemCardState target, CancellationToken token) {
            try {
                var translated = TranslateItemAsync(target, token);
                var equipment = LoadEquipmentAsync(target, token);
                var loaded = await App.Cards.LoadAsync(target.Origin, CardQuery.Item, target.Item.ItemId ?? 0, target.Item.ItemKind ?? 0, 0, null, target.Item.DataSource, token);
                if (!IsCurrent(target, token)) return;
                if (loaded.Page?.Item is { } item) { target.Item = item; target.DataScope = loaded.Page.DataScope; target.Cached = loaded.Cached; }
                CardStatusText.Text = L(loaded.Cached ? "Card.Cached" : loaded.Page != null ? "Card.Ready" : "Card.DataUnavailable");
                _ = LoadIconAsync(target, token); await RenderAsync();
                if (target.Item.ItemKind != (uint)GameItemKind.EventItem)
                    await Task.WhenAll(LoadPageAsync(target, CardQuery.Recipes, 0, token), LoadPageAsync(target, CardQuery.Sources, 0, token));
                await Task.WhenAll(translated, equipment);
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (IsCurrent(target, token)) CardStatusText.Text = L("Card.DataUnavailable") + " " + ex.Message; }
        }
        private async Task TranslateItemAsync(ItemCardState target, CancellationToken token) {
            if (!LocalizationHelper.LanguageCode.StartsWith("zh")) return;
            var text = await App.Cards.Chinese.ItemAsync(target.Item.ItemId ?? 0, target.Item.ItemKind == (uint)GameItemKind.EventItem, false, token);
            if (IsCurrent(target, token)) { target.Chinese = text; await RenderAsync(); }
        }
        private async Task TranslateSafelyAsync(ItemCardState target, CancellationToken token) {
            try {
                if (target.Chinese == null) await TranslateItemAsync(target, token);
                await Task.WhenAll(
                    LoadEquipmentAsync(target, token),
                    NamesAsync(target, "Item", target.Recipes?.Recipes.SelectMany(r => r.Ingredients.Select(i => i.Item.Id)) ?? Array.Empty<uint>(), token),
                    NamesAsync(target, "CraftType", target.Recipes?.Recipes.Select(r => r.CraftTypeId) ?? Array.Empty<uint>(), token),
                    NamesAsync(target, "ENpcResident", target.Sources?.Sources.Select(s => s.NpcId) ?? Array.Empty<uint>(), token),
                    NamesAsync(target, "GilShop", target.Sources?.Sources.Where(s => s.Kind == ItemSourceKind.GilShop).Select(s => s.Id) ?? Array.Empty<uint>(), token),
                    NamesAsync(target, "GatheringType", target.Sources?.Sources.Select(s => s.GatheringTypeId) ?? Array.Empty<uint>(), token),
                    NamesAsync(target, "PlaceName", target.Sources?.Sources.Select(s => s.Location?.PlaceNameId ?? 0) ?? Array.Empty<uint>(), token));
                if (IsCurrent(target, token)) await RenderAsync();
            } catch (OperationCanceledException) { }
        }
        private async Task LoadEquipmentAsync(ItemCardState target, CancellationToken token) {
            var snapshot = await App.Cards.EquipmentAsync(target.Origin);
            if (!IsCurrent(target, token)) return;
            target.Equipment = snapshot; await RenderAsync();
            await NamesAsync(target, "Item", snapshot?.Items.SelectMany(i => i.Materia.Select(m => m.Item.Id).Append(i.Item.ItemId ?? 0)) ?? Array.Empty<uint>(), token);
            await NamesAsync(target, "BaseParam", snapshot?.Items.SelectMany(i => i.Materia.Select(m => m.ParameterId).Concat(i.Item.ItemDetails?.Parameters.Select(p => p.Id) ?? Array.Empty<uint>())) ?? Array.Empty<uint>(), token);
            if (IsCurrent(target, token)) await RenderAsync();
        }
        private async void EquipmentChanged() { try { await LoadEquipmentAsync(state, cancellation.Token); } catch (OperationCanceledException) { } }
        private async Task NamesAsync(ItemCardState target, string table, IEnumerable<uint> ids, CancellationToken token) {
            if (!target.ShowChinese) return;
            var names = await App.Cards.Chinese.NamesAsync(table, ids.Where(id => id > 0), token);
            if (!IsCurrent(target, token)) return;
            if (!target.Names.TryGetValue(table, out var previous)) target.Names[table] = previous = new();
            foreach (var pair in names) previous[pair.Key] = pair.Value;
        }
        private async Task LoadPageAsync(ItemCardState target, CardQuery query, int page, CancellationToken token) {
            bool recipes = query == CardQuery.Recipes;
            if (recipes) target.RecipesLoading = true; else target.SourcesLoading = true;
            await RenderAsync();
            var result = await App.Cards.LoadAsync(target.Origin, query, target.Item.ItemId ?? 0, target.Item.ItemKind ?? 0, page, target.DataScope, target.Item.DataSource, token);
            if (!IsCurrent(target, token)) return;
            if (recipes) { target.Recipes = result.Page; target.RecipesLoading = false; target.RecipesStatus = result.Status; target.RecipesCached = result.Cached; }
            else { target.Sources = result.Page; target.SourcesLoading = false; target.SourcesStatus = result.Status; target.SourcesCached = result.Cached; }
            await RenderAsync();
            if (result.Page is not { } data) return;
            if (recipes) await Task.WhenAll(
                NamesAsync(target, "Item", data.Recipes.SelectMany(r => r.Ingredients.Select(i => i.Item.Id)), token),
                NamesAsync(target, "CraftType", data.Recipes.Select(r => r.CraftTypeId), token));
            else await Task.WhenAll(
                NamesAsync(target, "ENpcResident", data.Sources.Select(s => s.NpcId), token),
                NamesAsync(target, "GilShop", data.Sources.Where(s => s.Kind == ItemSourceKind.GilShop).Select(s => s.Id), token),
                NamesAsync(target, "GatheringType", data.Sources.Select(s => s.GatheringTypeId), token),
                NamesAsync(target, "PlaceName", data.Sources.Select(s => s.Location?.PlaceNameId ?? 0), token));
            if (IsCurrent(target, token)) await RenderAsync();
        }
        private async Task LoadIconAsync(ItemCardState target, CancellationToken token) {
            if (target.Item.ItemIconId is not > 0) return;
            string icon = target.Item.ItemIconId.Value.ToString("D6"), folder = (target.Item.ItemIconId.Value / 1000 * 1000).ToString("D6");
            try {
                string url = await LocalAssetCache.GetCachedImageAsync("icons/" + folder, icon + ".png", $"https://cafemaker.wakingsands.com/i/{folder}/{icon}.png", $"https://xivapi.com/i/{folder}/{icon}.png").WaitAsync(token);
                if (IsCurrent(target, token)) { target.IconUrl = url; await RenderAsync(); }
            } catch (OperationCanceledException) { }
        }
        private async Task RenderAsync() {
            if (closed) return;
            int version = ++renderVersion;
            try {
                await App.EnsureWebView2Async(ItemWebView); if (closed || version != renderVersion) return;
                if (!webReady) {
                    webReady = true; ItemWebView.CoreWebView2.WebMessageReceived += WebMessage;
                    ItemWebView.CoreWebView2.NavigationStarting += (_, args) => { if (args.Uri != documentUri && args.Uri != "about:blank") args.Cancel = true; };
                    ItemWebView.CoreWebView2.NavigationCompleted += async (_, _) => { try { await ItemWebView.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0," + restoreScroll.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"); } catch (Exception) { } };
                    ItemWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("cache.local", LocalAssetCache.CacheDir, CoreWebView2HostResourceAccessKind.Allow);
                }
                double scroll = 0;
                if (ReferenceEquals(renderedState, state)) double.TryParse(await ItemWebView.CoreWebView2.ExecuteScriptAsync("window.scrollY || 0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scroll);
                if (closed || version != renderVersion) return;
                restoreScroll = scroll; renderedState = state;
                AppWindow.Title = "XIVChat - " + state.Name;
                documentUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(GameCardHtml.Build(state)));
                ItemWebView.CoreWebView2.Navigate(documentUri);
            } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); if (!closed) CardStatusText.Text = ex.Message; }
        }
        private async void WebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
            if ((e.Source != documentUri && e.Source != "about:blank") || closed) return;
            try {
                var parts = e.TryGetWebMessageAsString().Split(':'); if (parts.Length != 2 || !int.TryParse(parts[1], out int number)) return;
                var target = state; var token = cancellation.Token;
                if (parts[0] is "item" or "materia") {
                    var item = (parts[0] == "item" ? target.Recipes?.Recipes.SelectMany(r => r.Ingredients.Select(i => i.Item)) : target.Equipment?.Items.SelectMany(i => i.Materia.Select(m => m.Item)))?.FirstOrDefault(i => i.Id == number);
                    if (item == null) return; if (parents.Count >= 16) return;
                    parents.Push(target); Open(item.Id, item.Kind == 1_000_000, item.Name, new TextChunk(item.Name) { ItemKind = item.Kind, ItemIconId = item.Icon }, target.Origin);
                } else if (parts[0] == "map" && number >= 0 && number < target.Sources?.Sources.Length) {
                    var map = target.Sources.Sources[number].Location;
                    if (map?.Id > 0 && map.X.HasValue && map.Y.HasValue) MapWindow.ShowMap(map.Id, map.X, map.Y, target.MapName(map), map.Filename, map.SizeFactor, target.Origin, target.Sources.Source);
                } else if (parts[0] == "compare" && target.Equipment?.Items.Any(i => i.Slot == number) == true) { target.SelectedSlot = number; await RenderAsync(); }
                else if (parts[0] is "recipes" or "sources") {
                    bool recipe = parts[0] == "recipes"; var page = recipe ? target.Recipes : target.Sources;
                    if (page == null || number < 0 || number >= page.PageCount || Math.Abs(number - page.Page) != 1 || (recipe ? target.RecipesLoading : target.SourcesLoading)) return;
                    await LoadPageAsync(target, recipe ? CardQuery.Recipes : CardQuery.Sources, number, token);
                }
            } catch (OperationCanceledException) { } catch (Exception ex) { CardStatusText.Text = L("Card.DataUnavailable") + " " + ex.Message; }
        }
        private void Back_Click(object sender, RoutedEventArgs e) {
            if (!parents.TryPop(out var previous)) return;
            Open(previous.Item.ItemId, previous.Item.ItemKind == 1_000_000, previous.Item.ItemName, previous.Item, previous.Origin);
        }
        private void Copy_Click(object sender, RoutedEventArgs e) { var data = new DataPackage(); data.SetText(GameCardHtml.Copy(state)); Clipboard.SetContent(data); CardStatusText.Text = L("Card.Copied"); }
        private async void Favorite_Click(object sender, RoutedEventArgs e) {
            var target = state;
            try { var favorite = await App.Cards.FavoriteAsync(target.Origin, target.Item, false, target.Name); if (ReferenceEquals(target, state)) { target.Origin = target.Origin with { FavoriteId = favorite.Id }; CardStatusText.Text = L("Card.Saved"); } }
            catch (Exception ex) { CardStatusText.Text = ex.Message; }
        }
        private async void Sources_Click(object sender, RoutedEventArgs e) { if (App.Window != null) await App.Window.ShowCardSourcesAsync(state.Origin); }
        private void Refresh_Click(object sender, RoutedEventArgs e) => Open(state.Item.ItemId, state.Item.ItemKind == 1_000_000, state.Item.ItemName, state.Item, state.Origin);
        private void Original_Click(object sender, RoutedEventArgs e) => LocalizeCard();
    }
}
