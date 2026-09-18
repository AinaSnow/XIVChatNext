using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed record EventKindOption(string Label, GameEventKind? Kind);
    public sealed record EventListItem(EventRow Row) {
        public string Heading => LocalizationHelper.GetString("Event." + Row.Event.Kind);
        public string Details => ((App)Application.Current).Presentation.EventDetails(Row.Source, Row.Event);
        public string Context => Row.Event.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " +
            (Row.Event.Owner is { } owner ? ((App)Application.Current).Presentation.OwnerLabel(Row.Source, Row.OwnerKey, owner) : LocalizationHelper.GetString("History.Unassigned"));
        public string Expiry => Row.Event.Kind == GameEventKind.DutyReady ? LocalizationHelper.GetString(
            Row.Event.ExpiresAt <= DateTime.UtcNow ? "Event.Expired" : "Event.ReturnToGame") : "";
        public string Glyph => Row.Event.Kind switch {
            GameEventKind.DutyReady => "\uE7C1", GameEventKind.Login => "\uE8D4", GameEventKind.Logout => "\uE8AC",
            GameEventKind.TerritoryChanged => "\uE707", GameEventKind.ConnectionLost => "\uE83A", _ => "\uE787",
        };
    }

    public partial class MainWindow {
        private readonly ObservableCollection<EventListItem> eventRows = new();
        private int eventLoadVersion;
        private int eventBadgeVersion;
        private bool eventSyncing;
        private bool markingEvents;
        private EventQuery? activeEventQuery;

        private void UpdateEventLocalizations() {
            NavEventsText.Text = EventsTitle.Text = L("Workbench.Events");
            EventOwnerPicker.Header = L("History.Owner"); EventKindPicker.Header = L("Event.Kind");
            EventRefreshButton.Content = L("Event.Refresh"); EventsMoreButton.Content = L("History.LoadOlder");
            EventsReadButton.Content = L("Event.MarkRead"); EventsEmpty.Text = L("Event.Empty"); EventCaptureHelp.Text = L("Event.CaptureHelp");
            var selected = (EventKindPicker.SelectedItem as EventKindOption)?.Kind;
            var kinds = new List<EventKindOption> { new(L("Event.AllKinds"), null) };
            kinds.AddRange(Enum.GetValues<GameEventKind>().Select(kind => new EventKindOption(L("Event." + kind), kind)));
            eventSyncing = true; EventKindPicker.ItemsSource = kinds;
            EventKindPicker.SelectedItem = kinds.FirstOrDefault(k => k.Kind == selected) ?? kinds[0]; eventSyncing = false;
            for (var i = 0; i < eventRows.Count; i++) eventRows[i] = new EventListItem(eventRows[i].Row);
            UpdateEventCapability(); _ = RefreshEventBadgeAsync();
        }

        private void UpdateEventCapability() {
            EventCapabilityText.Text = App.Connection == null ? L("Event.Offline") :
                App.Connection.SupportsGameEvents ? L("Event.Connected") : L("Event.Unsupported");
        }

        private async Task ShowEventsAsync(EventRow? focus = null) {
            var version = ++eventLoadVersion;
            SaveComposer(); UnselectConversation(); section = "events"; UpdateNavigation();
            ChatPanel.Visibility = HistoryPanel.Visibility = Visibility.Collapsed; EventsPanel.Visibility = Visibility.Visible;
            EventsList.ItemsSource = eventRows; UpdateEventCapability();
            var selected = (EventOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
            var owners = await App.Notifier.GetOwnersAsync();
            if (version != eventLoadVersion || section != "events") return;
            var choices = new List<HistoryOwnerOption> { new(L("History.AllOwners"), null) };
            choices.AddRange(owners.Select(owner => new HistoryOwnerOption(
                (owner.Identity is { } identity ? App.Presentation.OwnerLabel(owner.Source, owner.OwnerKey, identity) : L("History.Unassigned")) +
                " · " + owner.Source[..Math.Min(8, owner.Source.Length)], owner)));
            var source = focus?.Source ?? selected?.Source ?? App.Workbench.Source;
            var ownerKey = focus?.OwnerKey ?? selected?.OwnerKey ?? App.Workbench.OwnerKey;
            eventSyncing = true;
            EventOwnerPicker.ItemsSource = choices;
            EventOwnerPicker.SelectedItem = choices.FirstOrDefault(c => c.Owner?.Source == source && c.Owner?.OwnerKey == ownerKey) ?? choices[0];
            if (focus != null) EventKindPicker.SelectedIndex = 0;
            eventSyncing = false;
            await LoadEventsAsync(false, focus);
        }

        private void EventFilter_Changed(object sender, SelectionChangedEventArgs e) {
            if (initialized && !eventSyncing && section == "events") _ = LoadEventsAsync(false);
        }
        private void EventRefresh_Click(object sender, RoutedEventArgs e) => _ = ShowEventsAsync();
        private void EventsMore_Click(object sender, RoutedEventArgs e) => _ = LoadEventsAsync(true);
        private void EventsRead_Click(object sender, RoutedEventArgs e) => _ = MarkVisibleEventsReadAsync();

        private async Task LoadEventsAsync(bool more, EventRow? focus = null) {
            var version = ++eventLoadVersion;
            var owner = (EventOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
            var query = more && activeEventQuery != null ? activeEventQuery : new EventQuery(owner?.Source, owner?.OwnerKey,
                (EventKindPicker.SelectedItem as EventKindOption)?.Kind);
            if (more && eventRows.LastOrDefault() is { } last) query = query with { BeforeTimestampUtc = last.Row.Event.Timestamp, BeforeId = last.Row.Id };
            if (focus != null) query = query with { Source = focus.Source, OwnerKey = focus.OwnerKey, Kind = null,
                BeforeTimestampUtc = focus.Event.Timestamp, BeforeId = focus.Id + "\uffff" };
            Busy.IsActive = true; EventsMoreButton.IsEnabled = false;
            try {
                var rows = await App.Notifier.GetEventsAsync(query);
                if (version != eventLoadVersion || section != "events") return;
                if (!more) { eventRows.Clear(); activeEventQuery = query; }
                foreach (var row in rows) if (!eventRows.Any(item => item.Row.Id == row.Id)) eventRows.Add(new EventListItem(row));
                EventsMoreButton.IsEnabled = rows.Count == query.Limit;
                EventsEmpty.Visibility = V(eventRows.Count == 0);
                EventsSummary.Text = string.Format(L("Event.Count"), eventRows.Count);
                if (active) await MarkVisibleEventsReadAsync();
                if (version != eventLoadVersion || section != "events") return;
                if (focus != null && eventRows.FirstOrDefault(item => item.Row.Id == focus.Id) is { } target) {
                    EventsList.SelectedItem = target; EventsList.ScrollIntoView(target);
                }
            } finally { if (version == eventLoadVersion) Busy.IsActive = false; }
        }

        private void EventsChanged() {
            _ = RefreshEventBadgeAsync();
            if (section == "events" && !markingEvents) EventsSummary.Text = L("Event.NewAvailable");
        }
        private async Task RefreshEventBadgeAsync() {
            var version = ++eventBadgeVersion;
            var owner = App.Workbench.OwnerKey;
            var count = await App.Notifier.UnreadCountAsync(owner == null ? null : App.Workbench.Source, owner);
            if (version != eventBadgeVersion || stopping) return;
            EventsBadge.Text = count == 0 ? "" : count > 99 ? "99+" : count.ToString();
            ToolTipService.SetToolTip(NavEvents, string.Format(L("Event.Unread"), count));
        }
        private async Task MarkVisibleEventsReadAsync() {
            if (markingEvents) return;
            var unread = eventRows.Where(r => !r.Row.Read).Select(r => r.Row).Take(200).ToArray();
            if (unread.Length == 0) return;
            markingEvents = true;
            try {
                for (var i = 0; i < eventRows.Count; i++) if (unread.Any(row => row.Id == eventRows[i].Row.Id))
                    eventRows[i] = new EventListItem(eventRows[i].Row with { Read = true });
                await App.Notifier.MarkReadAsync(unread);
            } finally { markingEvents = false; }
        }

        internal bool IsReadingNotification(NotificationTarget target) {
            if (!active || ChatPanel.Visibility != Visibility.Visible || section == "history" || target.Source != App.Session.Source) return false;
            if (target.Kind == NotificationTargetKind.Conversation && selectedConversation is { } conversation &&
                conversation.State.Source == target.Source && conversation.State.OwnerKey == target.OwnerKey &&
                conversation.Key == ConversationIdentity.PeerKey(target.Peer) && ReferenceEquals(ChatHost.Content, conversationView))
                return conversationView?.FollowingLatest == true;
            if (target.OwnerKey != (App.Session.Player?.Identity?.Key ?? "unassigned:" + App.Session.Source)) return false;
            return selectedConversation == null && selectedChannel != null && target.Channel is { } channel &&
                channelViews.TryGetValue(selectedChannel, out var view) && ReferenceEquals(ChatHost.Content, view) && view.FollowingLatest &&
                selectedChannel.Filter.Types.Any(type => type.Allowed(new ChatCode(channel)));
        }

        internal async Task OpenNotificationTargetAsync(NotificationTarget target) {
            if (!target.Valid || stopping) return;
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
            Activate();
            try {
                if (target.Kind == NotificationTargetKind.Event) {
                    if (target.Source == "local" && target.OwnerKey == "setup" && target.RecordId == null) { SetupWizard.Show(); return; }
                    var row = target.RecordId == null ? null : await App.Notifier.GetEventAsync(target.RecordId);
                    if (row == null || row.Source != target.Source || row.OwnerKey != target.OwnerKey) { FooterStatus.Text = L("Notify.TargetMissing"); return; }
                    await ShowEventsAsync(row); return;
                }
                HistoryRow? message = null;
                if (target.RecordId != null && App.Session.Store is { } store) message = await store.GetMessageAsync(target.RecordId);
                if (message != null && (message.OwnerKey != target.OwnerKey || !message.Id.StartsWith(target.Source + "/", StringComparison.Ordinal))) return;
                var peer = message?.Message.TellPeer ?? target.Peer;
                if (target.Kind == NotificationTargetKind.Conversation && peer != null) {
                    if (App.Connection?.Available != true && message?.Message.Owner is { IsComplete: true } owner)
                        App.Workbench.SetContext(target.Source, owner);
                    if (App.Workbench.Source == target.Source && App.Workbench.OwnerKey == target.OwnerKey && App.Workbench.Open(peer) is { } conversation) {
                        Navigate("conversations"); ShowConversation(conversation); return;
                    }
                }
                if (message != null) {
                    // A notification from another role opens read-only history, preserving the active connection and composer owner.
                    SaveComposer(); UnselectConversation(); section = "history"; cardSourceOrigin = null; eventLoadVersion++; UpdateNavigation();
                    EventsPanel.Visibility = ChatPanel.Visibility = Visibility.Collapsed; HistoryPanel.Visibility = Visibility.Visible;
                    await LoadHistoryOwnersAsync();
                    historySyncing = true;
                    HistoryOwnerPicker.SelectedItem = historyOwners.FirstOrDefault(o => o.Owner?.Source == target.Source && o.Owner?.OwnerKey == target.OwnerKey);
                    historySyncing = false;
                    historyCancellation?.Cancel(); historyResults.Clear();
                    activeHistoryQuery = new HistoryQuery(Source: target.Source, OwnerKey: target.OwnerKey, RecordId: message.Id);
                    var result = new HistoryResult(message, ""); historyResults.Add(result); HistoryList.ItemsSource = historyResults;
                    HistoryList.SelectedItem = result; await OpenHistoryContextAsync(); return;
                }
                if (target.Source == App.Session.Source && target.OwnerKey == (App.Session.Player?.Identity?.Key ?? "unassigned:" + App.Session.Source) && target.Channel is { } channel &&
                    App.Config.Tabs.FirstOrDefault(tab => tab.Filter.Types.Any(type => type.Allowed(new ChatCode(channel)))) is { } view) {
                    Navigate("channels"); ChannelList.SelectedItem = view; SelectChannel(view); return;
                }
                FooterStatus.Text = L("Notify.TargetMissing");
            } catch (Exception ex) { FooterStatus.Text = L("Notify.OpenFailed") + " " + ex.Message; }
        }
    }
}
