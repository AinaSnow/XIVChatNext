using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public partial class MainWindow {
        private readonly ObservableCollection<CardFavorite> cardFavorites = new();
        private int favoritesVersion;
        private CardOrigin? cardSourceOrigin;
        private readonly Dictionary<string, CardOrigin> recentOrigins = new();
        private HistoryRow? recentBefore;
        private readonly Queue<HistoryRow> recentMessages = new();
        private int recentChunkIndex;
        private bool recentCanReadMore = true;
        private void UpdateCardLocalizations() {
            NavFavoritesText.Text = FavoritesTitle.Text = L("Workbench.Favorites"); FavoritesOpen.Content = L("Card.Open");
            FavoritesSources.Content = L("Card.MessageSources"); FavoritesDelete.Content = L("Card.Remove"); FavoritesMore.Content = L("Card.More");
            FavoriteCurrent.Content = L("Card.CurrentOwner"); FavoriteAll.Content = L("Card.AllOwners");
            FavoriteSaved.Content = L("Card.SavedCards"); FavoriteRecent.Content = L("Card.Recent");
            FavoriteSearch.Header = L("Card.Search"); FavoriteGroup.Header = L("Card.Group"); FavoriteTag.Header = L("Card.Tags");
            FavoriteApply.Content = L("History.Apply"); FavoritesEdit.Content = L("Card.Edit"); FavoritesSave.Content = L("Card.Favorite");
        }
        private void FavoriteFilter_Changed(object sender, SelectionChangedEventArgs e) { if (initialized && section == "favorites") _ = LoadFavoritesAsync(false); }
        private void FavoriteApply_Click(object sender, RoutedEventArgs e) => _ = LoadFavoritesAsync(false);
        private void FavoritesChanged() { if (section == "favorites") _ = LoadFavoritesAsync(false); }
        private async Task LoadFavoritesAsync(bool more) {
            int version = ++favoritesVersion; var origin = App.Cards.Origin();
            bool recent = FavoriteMode.SelectedIndex == 1, all = FavoriteScope.SelectedIndex == 1;
            if (!more) { cardFavorites.Clear(); recentOrigins.Clear(); recentBefore = null; recentMessages.Clear(); recentChunkIndex = 0; recentCanReadMore = true; }
            FavoritesList.ItemsSource = cardFavorites; FavoritesMore.IsEnabled = false;
            FavoritesEdit.Visibility = FavoritesDelete.Visibility = V(!recent); FavoritesSave.Visibility = V(recent);
            FavoriteGroup.IsEnabled = FavoriteTag.IsEnabled = !recent;
            FavoritesStatus.Text = L("Card.Loading");
            try {
                if (App.Session.Store is not { } store) { FavoritesStatus.Text = L("History.Unavailable"); return; }
                if (recent) {
                    if (recentMessages.Count == 0 && recentCanReadMore) {
                        var messages = await store.SearchAsync(new(Source: all ? null : origin.Source, OwnerKey: all ? null : origin.OwnerKey,
                            Text: FavoriteSearch.Text.Trim(), BeforeRow: recentBefore?.RowId ?? long.MaxValue, BeforeTimestampUtc: recentBefore?.Message.Timestamp, Limit: 100));
                        if (version != favoritesVersion || section != "favorites") return;
                        foreach (var row in messages) recentMessages.Enqueue(row);
                        recentBefore = messages.LastOrDefault() ?? recentBefore; recentCanReadMore = messages.Count == 100;
                    }
                    int added = 0;
                    while (recentMessages.TryPeek(out var row) && added < 100) {
                        if (recentChunkIndex >= row.Message.Chunks.Count) { recentMessages.Dequeue(); recentChunkIndex = 0; continue; }
                        int index = recentChunkIndex++;
                        if (row.Message.Chunks[index] is TextChunk chunk) {
                            string id = row.Id + ":" + index; bool map = chunk.MapId is > 0 and < 100_000;
                            if (!map && chunk.ItemId is not > 0) continue;
                            var saved = MessagePack.MessagePackSerializer.Deserialize<TextChunk>(MessagePack.MessagePackSerializer.Serialize(chunk));
                            if (!map) { var identity = GameItemIdentity.Resolve(saved.ItemId ?? 0, saved.ItemKind, saved.IsHq == true); saved.ItemId = identity.Id; saved.ItemKind = (uint)identity.Kind; saved.ItemName ??= saved.Content; }
                            var card = new CardFavorite { Id = id, Source = row.Source, OwnerKey = row.OwnerKey, Snapshot = saved, IsMap = map,
                                Name = (map ? saved.MapPlaceName : saved.ItemName) ?? saved.Content, SavedAt = new DateTimeOffset(row.Message.Timestamp).ToUnixTimeMilliseconds() };
                            cardFavorites.Add(card); added++; recentOrigins[id] = new(row.Source, row.OwnerKey, row.Message);
                        }
                    }
                    FavoritesMore.IsEnabled = recentMessages.Count > 0 || recentCanReadMore;
                    FavoritesStatus.Text = L("Card.RecentScope") + " · " + cardFavorites.Count; return;
                }
                var rows = await store.GetCardFavoritesAsync(all ? null : origin.Source, all ? null : origin.OwnerKey, cardFavorites.Count,
                    search: FavoriteSearch.Text.Trim(), group: FavoriteGroup.Text.Trim(), tag: FavoriteTag.Text.Trim());
                if (version != favoritesVersion || section != "favorites") return;
                foreach (var row in rows) cardFavorites.Add(row);
                FavoritesStatus.Text = cardFavorites.Count == 0 ? L("Card.NoFavorites") : string.Format(L("History.Results"), cardFavorites.Count);
                FavoritesMore.IsEnabled = rows.Count == 100;
            } catch (Exception ex) { if (version == favoritesVersion) FavoritesStatus.Text = ex.Message; }
        }
        private void FavoritesMore_Click(object sender, RoutedEventArgs e) => _ = LoadFavoritesAsync(true);
        private CardOrigin FavoriteOrigin(CardFavorite favorite) => recentOrigins.TryGetValue(favorite.Id, out var origin) ? origin : new(favorite.Source, favorite.OwnerKey, FavoriteId: favorite.Id);
        private void FavoriteOpen_Click(object sender, RoutedEventArgs e) {
            if (FavoritesList.SelectedItem is not CardFavorite favorite) return;
            var item = favorite.Snapshot; var origin = FavoriteOrigin(favorite);
            if (favorite.IsMap) MapWindow.ShowMap(item.MapId, item.MapX, item.MapY, favorite.Name, item.MapFilenameId, item.MapSizeFactor, origin, item.DataSource);
            else ItemWindow.ShowItem(item.ItemId, item.ItemKind == 1_000_000, favorite.Name, item, origin);
        }
        private async void FavoriteSources_Click(object sender, RoutedEventArgs e) {
            if (FavoritesList.SelectedItem is CardFavorite favorite) await ShowCardSourcesAsync(FavoriteOrigin(favorite));
        }
        private async void FavoriteSave_Click(object sender, RoutedEventArgs e) {
            if (FavoritesList.SelectedItem is not CardFavorite favorite) return;
            try { await App.Cards.FavoriteAsync(FavoriteOrigin(favorite), favorite.Snapshot, favorite.IsMap, favorite.Name); FavoritesStatus.Text = L("Card.Saved"); }
            catch (Exception ex) { FavoritesStatus.Text = ex.Message; }
        }
        private async void FavoriteEdit_Click(object sender, RoutedEventArgs e) {
            if (FavoritesList.SelectedItem is not CardFavorite favorite || App.Session.Store is not { } store || dialogOpen) return;
            if (App.Presentation.Enabled) { FavoritesStatus.Text = L("Privacy.EditAfterDisable"); return; }
            dialogOpen = true;
            try {
                var group = new TextBox { Header = L("Card.Group"), Text = favorite.Group, MaxLength = 128 };
                var tags = new TextBox { Header = L("Card.Tags"), Text = favorite.Tags, MaxLength = 512 };
                var note = new TextBox { Header = L("History.Note"), Text = favorite.Note, MaxLength = 8192, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 220 };
                var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(group); panel.Children.Add(tags); panel.Children.Add(note);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = favorite.Name, Content = panel, PrimaryButtonText = L("Dialog.Save"), CloseButtonText = L("Dialog.Cancel") };
                if (await ShowIdentityEditorAsync(dialog) != ContentDialogResult.Primary) return;
                await store.UpdateCardFavoriteMetadataAsync(favorite.Id, favorite.Source, favorite.OwnerKey, group.Text.Trim(), tags.Text.Trim(), note.Text);
                if (section == "favorites") await LoadFavoritesAsync(false);
            } catch (Exception ex) { FavoritesStatus.Text = ex.Message; } finally { dialogOpen = false; }
        }
        private async void FavoriteDelete_Click(object sender, RoutedEventArgs e) {
            if (FavoritesList.SelectedItem is not CardFavorite favorite || App.Session.Store is not { } store) return;
            try { await store.DeleteCardFavoriteAsync(favorite.Id, favorite.Source, favorite.OwnerKey); cardFavorites.Remove(favorite); }
            catch (Exception ex) { FavoritesStatus.Text = ex.Message; }
        }
        public async Task ShowCardSourcesAsync(CardOrigin origin) {
            SaveComposer(); UnselectConversation(); historyCancellation?.Cancel(); Busy.IsActive = false;
            section = "history"; eventLoadVersion++; favoritesVersion++; cardSourceOrigin = origin;
            FavoritesPanel.Visibility = EventsPanel.Visibility = ChatPanel.Visibility = Visibility.Collapsed; HistoryPanel.Visibility = Visibility.Visible;
            UpdateNavigation(); HistoryList.ItemsSource = historyResults; Activate();
            await LoadCardSourcesAsync(origin, false);
        }
        private async Task LoadCardSourcesAsync(CardOrigin origin, bool more) {
            if (!more) historyResults.Clear(); HistoryMoreButton.IsEnabled = false;
            try {
                if (App.Session.Store is not { } store) { HistorySummary.Text = L("History.Unavailable"); return; }
                if (origin.FavoriteId != null) {
                    var rows = await store.GetCardSourcesAsync(origin.FavoriteId, origin.Source, origin.OwnerKey, historyResults.Count);
                    if (!ReferenceEquals(cardSourceOrigin, origin) || section != "history") return;
                    foreach (var row in rows) historyResults.Add(new(row, "")); HistoryMoreButton.IsEnabled = rows.Count == 100;
                } else if (origin.Message is { } message) {
                    var id = message.LocalStorageId ?? HistoryStore.StorageId(origin.Source, message);
                    var row = await store.GetMessageAsync(id);
                    if (!ReferenceEquals(cardSourceOrigin, origin) || section != "history") return;
                    if (row != null && row.Source == origin.Source && row.OwnerKey == origin.OwnerKey) historyResults.Add(new(row, ""));
                    else {
                        contextView?.Dispose(); var tab = new Tab(L("Card.MessageSources")) { Filter = new EverythingFilter() };
                        tab.MergeHistory(new[] { message }, App.Config); contextView = new Controls.ChatMessageList(tab); ChatHost.Content = contextView;
                        HistoryPanel.Visibility = Visibility.Collapsed; ChatPanel.Visibility = Visibility.Visible; ComposerPanel.Visibility = Visibility.Collapsed;
                        ChatTitle.Text = L("Card.MessageSources"); ChatSubtitle.Text = message.Timestamp.ToLocalTime().ToString("g");
                        UpdateChatActions(false); EditChannelButton.Visibility = Visibility.Collapsed; BackHistoryButton.Visibility = Visibility.Visible;
                        return;
                    }
                }
                HistorySummary.Text = historyResults.Count == 0 ? L("Card.NoMessageSources") : L("Card.MessageSources") + " · " + historyResults.Count;
                if (!more && historyResults.Count > 0) HistoryList.SelectedIndex = 0;
                if (!more && origin.Message != null && historyResults.Count == 1) await OpenHistoryContextAsync();
            } catch (Exception ex) { if (ReferenceEquals(cardSourceOrigin, origin)) HistorySummary.Text = ex.Message; }
        }
    }
}
