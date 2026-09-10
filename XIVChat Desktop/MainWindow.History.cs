using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using XIVChatCommon.Message;
using XIVChatCommon;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed record HistoryOwnerOption(string Label, HistoryOwner? Owner);
    public sealed record HistoryChannelOption(string Label, ushort? Channel);
    public sealed record HistoryResult(HistoryRow Row, string Query) {
        public string Heading => (Row.Bookmarked ? "★  " : "") + Row.Message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + Row.Message.Owner?.Name + " · " + Row.Message.SenderText + " · " + Row.Message.Channel;
        public string Content => Row.Message.ContentText;
        public string Note => Row.Note;
    }
    public partial class MainWindow {
        private CancellationTokenSource? historyCancellation;
        private List<HistoryOwnerOption> historyOwners = new();
        private readonly ObservableCollection<HistoryResult> historyResults = new();
        private HistoryQuery? activeHistoryQuery;
        private bool historySyncing;
        private void UpdateHistoryLocalizations() {
            HistoryTitle.Text = L("Workbench.History"); HistoryOwnerPicker.Header = L("History.Owner");
            HistoryChannelPicker.Header = L("Workbench.Channel"); HistoryPerson.Header = L("History.Person");
            HistoryFrom.Header = L("History.From"); HistoryUntil.Header = L("History.Until");
            HistoryBookmarks.Content = L("History.BookmarksOnly"); HistoryApply.Content = L("History.Apply");
            HistoryMoreButton.Content = L("History.LoadOlder"); HistoryContextButton.Content = L("History.Context");
            HistoryNoteButton.Content = L("History.Note"); UpdateHistorySelection();
            var selected = (HistoryChannelPicker.SelectedItem as HistoryChannelOption)?.Channel;
            historySyncing = true;
            var channels = new List<HistoryChannelOption> { new(L("History.AllChannels"), null) };
            channels.AddRange(Enum.GetValues<ChatType>().Distinct().Select(c => new HistoryChannelOption(c.ToString(), (ushort)c)));
            HistoryChannelPicker.ItemsSource = channels;
            HistoryChannelPicker.SelectedItem = channels.FirstOrDefault(c => c.Channel == selected) ?? channels[0];
            historySyncing = false;
        }
        private async Task LoadHistoryOwnersAsync() {
            if (App.Session.Store is not { } store) return;
            try {
                var saved = (HistoryOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
                var owners = await store.GetOwnersAsync();
                historyOwners = new() { new(L("History.AllOwners"), null) };
                historyOwners.AddRange(owners.Select(o => new HistoryOwnerOption((o.Identity is { } who ? who.Name + " @ " + who.HomeWorld : L("History.Unassigned")) + " · " + o.Source, o)));
                historySyncing = true; HistoryOwnerPicker.ItemsSource = historyOwners;
                HistoryOwnerPicker.SelectedItem = historyOwners.FirstOrDefault(o => o.Owner?.Source == saved?.Source && o.Owner?.OwnerKey == saved?.OwnerKey) ?? historyOwners[0];
                historySyncing = false;
            } catch (Exception ex) { HistorySummary.Text = L("History.Unavailable") + " " + ex.Message; }
        }
        private async void ShowHistory() {
            SaveComposer(); UnselectConversation();
            ChatPanel.Visibility = Visibility.Collapsed; HistoryPanel.Visibility = Visibility.Visible;
            HistoryList.ItemsSource = historyResults;
            await LoadHistoryOwnersAsync();
            if (section == "history") await SearchHistoryAsync(false);
        }
        private void GlobalSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => Navigate("history");
        private void HistoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!initialized || historySyncing) return;
            if (ReferenceEquals(sender, HistoryOwnerPicker) && App.Connection?.Available != true && (HistoryOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner is { Identity.IsComplete: true } owner)
                App.Workbench.SetContext(owner.Source, owner.Identity);
            if (section == "history") _ = SearchHistoryAsync(false);
        }
        private void HistoryApply_Click(object sender, RoutedEventArgs e) => _ = SearchHistoryAsync(false);
        private void HistoryMore_Click(object sender, RoutedEventArgs e) => _ = SearchHistoryAsync(true);
        private async Task SearchHistoryAsync(bool more) {
            historyCancellation?.Cancel(); historyCancellation?.Dispose(); historyCancellation = new();
            var token = historyCancellation.Token;
            if (App.Session.Store is not { } store) { HistorySummary.Text = L("History.Unavailable"); return; }
            var owner = (HistoryOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
            var query = more && activeHistoryQuery != null ? activeHistoryQuery : new HistoryQuery(
                Source: owner?.Source, OwnerKey: owner?.OwnerKey, Text: GlobalSearch.Text.Trim(),
                Channel: (HistoryChannelPicker.SelectedItem as HistoryChannelOption)?.Channel,
                Person: HistoryPerson.Text.Trim(), FromUtc: HistoryFrom.Date?.Date.ToUniversalTime(),
                UntilUtc: HistoryUntil.Date?.Date.AddDays(1).ToUniversalTime(), BookmarksOnly: HistoryBookmarks.IsChecked == true, Limit: 100);
            if (query.FromUtc > query.UntilUtc) { HistorySummary.Text = L("History.InvalidDates"); return; }
            if (more && historyResults.LastOrDefault() is { } last) query = query with { BeforeRow = last.Row.RowId, BeforeTimestampUtc = last.Row.Message.Timestamp };
            else { historyResults.Clear(); activeHistoryQuery = query; }
            Busy.IsActive = true; HistoryMoreButton.IsEnabled = false;
            try {
                var rows = await store.SearchAsync(query, token);
                if (token.IsCancellationRequested) return;
                foreach (var row in rows) historyResults.Add(new HistoryResult(row, query.Text ?? ""));
                HistoryMoreButton.IsEnabled = rows.Count == 100;
                HistorySummary.Text = string.Format(L("History.Results"), historyResults.Count);
            } catch (OperationCanceledException) { }
            catch (Exception ex) { HistorySummary.Text = L("History.Unavailable") + " " + ex.Message; }
            finally { if (!token.IsCancellationRequested) Busy.IsActive = false; }
        }
        private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHistorySelection();
        private void UpdateHistorySelection() {
            var selected = HistoryList.SelectedItem as HistoryResult;
            HistoryContextButton.IsEnabled = HistoryNoteButton.IsEnabled = HistoryBookmarkButton.IsEnabled = selected != null;
            HistoryBookmarkButton.Content = L(selected?.Row.Bookmarked == true ? "History.Unbookmark" : "History.Bookmark");
        }
        private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => _ = OpenHistoryContextAsync();
        private void HistoryContext_Click(object sender, RoutedEventArgs e) => _ = OpenHistoryContextAsync();
        private async Task OpenHistoryContextAsync() {
            if (HistoryList.SelectedItem is not HistoryResult result || App.Session.Store is not { } store) return;
            try {
                var rows = await store.GetContextAsync(result.Row.Id);
                if (section != "history" || !ReferenceEquals(HistoryList.SelectedItem, result)) return;
                if (rows.Count == 0) { HistorySummary.Text = L("History.Removed"); return; }
                contextView?.Dispose();
                var tab = new Tab(L("History.Context")) { Filter = new EverythingFilter() };
                tab.MergeHistory(rows.Select(r => r.Message), App.Config);
                contextView = new Controls.ChatMessageList(tab);
                ChatHost.Content = contextView; ChatPanel.Visibility = Visibility.Visible; HistoryPanel.Visibility = Visibility.Collapsed;
                ComposerPanel.Visibility = Visibility.Collapsed; ChatTitle.Text = L("History.Context");
                ChatSubtitle.Text = result.Row.Message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + result.Row.Message.ContentText;
                UpdateChatActions(false); EditChannelButton.Visibility = Visibility.Collapsed; BackHistoryButton.Visibility = Visibility.Visible;
                contextView.Loaded += (_, _) => contextView?.ScrollToMessage(result.Row.Message);
            } catch (Exception ex) { HistorySummary.Text = L("History.Unavailable") + " " + ex.Message; }
        }
        private void BackHistory_Click(object sender, RoutedEventArgs e) { ChatPanel.Visibility = Visibility.Collapsed; HistoryPanel.Visibility = Visibility.Visible; }
        private async void HistoryBookmark_Click(object sender, RoutedEventArgs e) {
            if (HistoryList.SelectedItem is HistoryResult result) await AnnotateHistoryAsync(result, result.Row.Note, !result.Row.Bookmarked);
        }
        private async void HistoryNote_Click(object sender, RoutedEventArgs e) {
            if (HistoryList.SelectedItem is not HistoryResult result) return;
            var note = await EditTextAsync(L("History.Note"), result.Row.Note);
            if (note != null) await AnnotateHistoryAsync(result, note, result.Row.Bookmarked);
        }
        private async Task AnnotateHistoryAsync(HistoryResult result, string note, bool bookmarked) {
            try {
                if (App.Session.Store is not { } store) return;
                await store.AnnotateAsync(result.Row.Id, note, bookmarked);
                var index = historyResults.IndexOf(result);
                if (index >= 0) { var updated = result with { Row = result.Row with { Note = note, Bookmarked = bookmarked } }; historyResults[index] = updated; HistoryList.SelectedItem = updated; }
            } catch (Exception ex) { HistorySummary.Text = L("History.Unavailable") + " " + ex.Message; }
        }
    }
}
