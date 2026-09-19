using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed record FriendRow(Player Player, CharacterIdentity? Peer, string Name, string World, string Status) { public override string ToString() => Name; }
    public partial class MainWindow : INotifyPropertyChanged {
        public App App => (App)Application.Current;
        public List<ServerMessage> Messages => App.Session.Messages;
        public TextBlock LoggedInAsText => LoggedInAs;
        public TextBlock LoggedInAsSeparatorText => LoggedInAsSeparator;
        public TextBlock CurrentWorldText => CurrentWorld;
        public TextBlock CurrentWorldSeparatorText => CurrentWorldSeparator;
        public TextBlock LocationText => Location;
        public HyperlinkButton LocationButton => LocationBtn;
        public PlayerData? CurrentPlayerData { get; set; }
        public string InputPlaceholder => App.Connection?.Available == true ? L("Workbench.TypeMessage") : L("Status.Disconnected");
        private readonly Dictionary<Tab, Controls.ChatMessageList> channelViews = new();
        private readonly Dictionary<string, string> channelDrafts = new();
        private readonly ObservableCollection<ConversationModel> visibleConversations = new();
        private readonly ObservableCollection<FriendRow> visibleFriends = new();
        private string section = "channels";
        private Tab? selectedChannel;
        private ConversationModel? selectedConversation;
        private Tab? conversationTab;
        private Controls.ChatMessageList? conversationView;
        private Controls.ChatMessageList? contextView;
        private readonly HashSet<string> conversationSeen = new();
        private HistoryRow? conversationOldest;
        private CancellationTokenSource? conversationLoad;
        private bool syncing;
        private bool initialized;
        private bool stopping;
        private bool allowClose;
        private string? activeChannelDraftKey;
        private bool active;
        private readonly DispatcherTimer presenceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private string? selectedOwner;
        private Connection? observedConnection;
        private static string L(string key) => LocalizationHelper.GetString(key);
        private static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public MainWindow() {
            InitializeComponent(); ThemeHelper.InitializeWindow(this);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 820));
            ConversationList.ItemsSource = visibleConversations;
            FriendList.ItemsSource = visibleFriends;
            ChannelList.ItemsSource = App.Config.Tabs;
            App.Config.Tabs.CollectionChanged += TabsChanged;
            App.Config.Saved += ConfigSaved;
            App.Updates.Changed += UpdateReleaseBanner;
            App.Presentation.Changed += UpdatePrivacyDisplay;
            App.PropertyChanged += AppChanged;
            App.Workbench.Changed += WorkbenchChanged;
            App.Session.MessagesChanged += SessionMessagesChanged;
            App.Session.Cleared += SessionCleared;
            App.Session.Friends.Changed += RefreshFriendRows;
            App.Session.Friends.Presence.Changed += RefreshFriendRows;
            App.Notifier.EventsChanged += EventsChanged;
            App.Cards.FavoritesChanged += FavoritesChanged;
            presenceTimer.Tick += PresenceTick;
            presenceTimer.Start();
            Root.SizeChanged += (_, _) => SidebarColumn.Width = new GridLength(Root.ActualWidth < 1000 ? 212 : 252);
            Activated += (_, args) => { active = args.WindowActivationState != WindowActivationState.Deactivated; ReadCurrent(); };
            AppWindow.Closing += async (_, args) => {
                if (allowClose) return;
                args.Cancel = true;
                await App.Workspace.CloseMainAsync();
            };
            Closed += (_, _) => DisposeViews();
            initialized = true;
            InitializePopoutDrag();
            Localize.BindWindow(this, UpdateLocalizations); ObserveConnection();
            ChannelList.SelectedIndex = App.Config.Tabs.Count > 0 ? 0 : -1;
            Navigate("channels");
            Root.Loaded += async (_, _) => {
                await LoadHistoryOwnersAsync();
                if (App.Session.Player == null && App.Workbench.OwnerKey == null && historyOwners.FirstOrDefault(o => o.Owner?.Identity?.IsComplete == true) is { Owner: { } owner })
                    App.Workbench.SetContext(owner.Source, owner.Identity);
            };
        }
        public void UpdateLocalizations() {
            if (!initialized) return;
            Title = "XIVChat Next";
            MenuConnect.Text = L("Menu.Connect"); MenuDisconnect.Text = L("Menu.Disconnect"); MenuMap.Text = L("Workbench.Map");
            MenuScreenshot.Text = L("Screenshot.Title");
            MenuLayout.Text = L("Windows.Layouts");
            ToolTipService.SetToolTip(PopoutButton, L("Windows.Popout"));
            ToolTipService.SetToolTip(PopoutDragHandle, L("Windows.DragOut"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PopoutButton, L("Windows.Popout"));
            ToolTipService.SetToolTip(ScreenshotButton, L("Screenshot.Title"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ScreenshotButton, L("Screenshot.Title"));
            MenuRefreshFriends.Text = L("FriendList.Refresh"); MenuExport.Text = L("Menu.Export"); MenuConfig.Text = L("Menu.Config"); MenuExit.Text = L("Menu.Exit");
            NavConversationsText.Text = L("Workbench.Conversations"); NavChannelsText.Text = L("Workbench.Channels");
            NavFriendsText.Text = L("Workbench.Friends"); NavHistoryText.Text = L("Workbench.History");
            UpdateEventLocalizations();
            UpdateCardLocalizations();
            GlobalSearch.PlaceholderText = L("Workbench.Search"); ListSearch.PlaceholderText = L("Workbench.Filter");
            SendButton.Content = L("Workbench.Send"); RestoreDraftButton.Content = L("Conversation.RestoreDraft");
            SymbolPicker.Attach(Composer); SymbolPicker.Localize();
            RefreshFriendsButton.Content = L("FriendList.Refresh");
            ToolTipService.SetToolTip(SettingsButton, L("Menu.Config"));
            ToolTipService.SetToolTip(AddViewButton, L("Workbench.Add"));
            ToolTipService.SetToolTip(PinConversationButton, L("Conversation.Pin"));
            ToolTipService.SetToolTip(ConversationNoteButton, L("Conversation.Note"));
            ToolTipService.SetToolTip(AvatarButton, L("Avatar.Title"));
            ToolTipService.SetToolTip(LoadOlderButton, L("History.LoadOlder"));
            ToolTipService.SetToolTip(EditChannelButton, L("Workbench.EditChannel"));
            ToolTipService.SetToolTip(BackHistoryButton, L("History.Back"));
            ChannelSwitchButton.Flyout = CreateChannelFlyout();
            foreach (var view in channelViews.Values) view.UpdateLocalizations();
            conversationView?.UpdateLocalizations(); UpdateHistoryLocalizations();
            RefreshFriendRows(); UpdateNavigation(); UpdateReady(); UpdatePrivacyDisplay();
            UpdateReleaseBanner();
        }
        private void ConfigSaved() { if (initialized) { UpdateLocalizations(); UpdateReady(); if (section == "channels" && selectedChannel != null) ChatTitle.Text = App.Presentation.Text(selectedChannel.Name); } }
        private void AppChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(App.Connection)) ObserveConnection();
        }
        private void ObserveConnection() {
            if (observedConnection != null) observedConnection.PropertyChanged -= ConnectionChanged;
            observedConnection = App.Connection;
            if (observedConnection != null) observedConnection.PropertyChanged += ConnectionChanged;
            UpdateReady(); UpdateEventCapability();
        }
        private void ConnectionChanged(object? sender, PropertyChangedEventArgs e) => App.Dispatch(() => { UpdateReady(); UpdateEventCapability(); });
        private void WorkbenchChanged() {
            if (!initialized) return;
            if (selectedOwner != App.Workbench.Source + "/" + App.Workbench.OwnerKey) {
                SaveComposer(); selectedOwner = App.Workbench.Source + "/" + App.Workbench.OwnerKey;
                if (section == "favorites") _ = LoadFavoritesAsync(false);
                if (selectedConversation != null) UnselectConversation();
                if (section is "conversations" or "friends") ShowEmpty();
                else if (section == "channels" && selectedChannel != null) {
                    activeChannelDraftKey = ChannelDraftKey(selectedChannel);
                    syncing = true; Composer.Text = channelDrafts.GetValueOrDefault(activeChannelDraftKey, ""); syncing = false;
                }
            }
            FilterConversations(); UpdateNavigation(); UpdateReady(); ReadCurrent(); _ = RefreshEventBadgeAsync();
        }
        private void SessionCleared() { conversationTab?.ClearMessages(); conversationSeen.Clear(); }
        private void TabsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            foreach (var tab in channelViews.Keys.Where(t => !App.Config.Tabs.Contains(t)).ToArray()) { channelViews[tab].Dispose(); channelViews.Remove(tab); }
            if (selectedChannel != null && !App.Config.Tabs.Contains(selectedChannel)) selectedChannel = null;
            if (section == "channels" && selectedChannel == null && App.Config.Tabs.Count > 0) ChannelList.SelectedIndex = 0;
            UpdateNavigation();
        }
        private void Navigate_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string target }) Navigate(target); }
        internal void Navigate(string target) {
            SaveComposer(); section = target;
            favoritesVersion++; cardSourceOrigin = null;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            eventLoadVersion++;
            if (target != "history") { historyCancellation?.Cancel(); Busy.IsActive = false; }
            syncing = true; ListSearch.Text = ""; syncing = false;
            UpdateNavigation();
            EventsPanel.Visibility = Visibility.Collapsed;
            if (target == "favorites") { ChatPanel.Visibility = HistoryPanel.Visibility = Visibility.Collapsed; FavoritesPanel.Visibility = Visibility.Visible; _ = LoadFavoritesAsync(false); return; }
            if (target == "events") { _ = ShowEventsAsync(); return; }
            if (target == "history") { ShowHistory(); return; }
            ChatPanel.Visibility = Visibility.Visible; HistoryPanel.Visibility = Visibility.Collapsed;
            BackHistoryButton.Visibility = Visibility.Collapsed;
            if (target == "channels") {
                if (selectedChannel != null) SelectChannel(selectedChannel);
                else if (App.Config.Tabs.Count > 0) { ChannelList.SelectedIndex = 0; SelectChannel(App.Config.Tabs[0]); }
                else ShowEmpty();
            } else if (target == "conversations") {
                FilterConversations();
                if (selectedConversation != null) ShowConversation(selectedConversation);
                else if (visibleConversations.Count > 0) {
                    syncing = true; ConversationList.SelectedIndex = 0; syncing = false;
                    ShowConversation(visibleConversations[0]);
                }
                else ShowEmpty();
            } else { RefreshFriendRows(); if (selectedConversation == null) ShowEmpty(); }
        }
        private void UpdateNavigation() {
            if (section != "favorites") FavoritesPanel.Visibility = Visibility.Collapsed;
            SectionTitle.Text = L("Workbench." + (section switch { "conversations" => "Conversations", "friends" => "Friends", "history" => "History", "events" => "Events", "favorites" => "Favorites", _ => "Channels" }));
            foreach (var button in new[] { NavConversations, NavChannels, NavFriends, NavHistory, NavEvents, NavFavorites })
                button.Background = new SolidColorBrush((string)button.Tag == section ? Windows.UI.Color.FromArgb(55, 74, 144, 226) : Microsoft.UI.Colors.Transparent);
            ChannelList.Visibility = V(section == "channels"); ConversationList.Visibility = V(section == "conversations"); FriendList.Visibility = V(section == "friends");
            HistoryFilters.Visibility = V(section == "history"); ListSearch.Visibility = V(section is "friends" or "conversations");
            EventFilters.Visibility = V(section == "events");
            FavoriteFilters.Visibility = V(section == "favorites");
            AddViewButton.Visibility = V(section is "channels" or "conversations"); RefreshFriendsButton.Visibility = V(section == "friends");
            SidebarEmpty.Visibility = V(section == "conversations" && visibleConversations.Count == 0 || section == "friends" && visibleFriends.Count == 0 || section == "channels" && App.Config.Tabs.Count == 0);
            SidebarEmpty.Text = App.Workbench.OwnerKey == null && section != "channels" ? L("Workbench.ConnectFirst") : L(section == "friends" ? "FriendList.Empty" : "Conversation.Empty");
            if (section != "friends") SidebarStatus.Text = App.Workbench.Owner is { } owner ? App.Presentation.Identity(owner).Label : L("Workbench.OfflineHistory");
        }
        private void ListSearch_TextChanged(object sender, TextChangedEventArgs e) { if (!initialized || syncing) return; if (section == "friends") RefreshFriendRows(); else FilterConversations(); UpdateNavigation(); }
        private void FilterConversations() {
            var search = ListSearch.Text.Trim();
            var desired = App.Workbench.Conversations.Where(c => section != "conversations" || search.Length == 0 || (c.Name + " " + c.World + " " + (App.Presentation.Enabled ? App.Presentation.Text(c.Note) : c.Peer.Name + " " + c.Peer.HomeWorld + " " + c.Note)).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            syncing = true;
            foreach (var item in visibleConversations.Where(c => !desired.Contains(c)).ToArray()) visibleConversations.Remove(item);
            for (int i = 0; i < desired.Length; i++) { var existing = visibleConversations.IndexOf(desired[i]); if (existing < 0) visibleConversations.Insert(i, desired[i]); else if (existing != i) visibleConversations.Move(existing, i); }
            if (selectedConversation != null && visibleConversations.Contains(selectedConversation)) ConversationList.SelectedItem = selectedConversation;
            syncing = false;
        }
        private void RefreshFriendRows() {
            if (!initialized) return;
            var state = App.Session.Friends;
            var search = section == "friends" ? ListSearch.Text.Trim() : "";
            var snapshot = state.Snapshot;
            syncing = true;
            var selectedFriendCid = (FriendList.SelectedItem as FriendRow)?.Player.ContentId;
            var desiredFriends = new List<FriendRow>();
            if (snapshot != null && snapshot.Owner?.Key == App.Workbench.OwnerKey && state.Source == App.Workbench.Source) {
                foreach (var player in snapshot.Players.OrderByDescending(p => p.HasStatus(PlayerStatus.Online)).ThenBy(p => p.IdentityUnavailable).ThenBy(p => p.Name)) {
                    var world = player.HomeWorld > 0 ? player.HomeWorldName ?? Util.WorldName(player.HomeWorld) ?? "" : "";
                    var identity = player.IdentityUnavailable ? null : new CharacterIdentity { Name = player.Name ?? "", HomeWorldId = player.HomeWorld, HomeWorld = world, ContentId = player.ContentId };
                    var display = App.Presentation.Identity(identity);
                    var name = identity == null ? L("FriendList.IdentityUnavailable") : display.Name;
                    var searchable = name + " " + display.World + (App.Presentation.Enabled ? "" : " " + player.Name + " " + world);
                    if (search.Length > 0 && !searchable.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    world = display.World;
                    var status = PresenceText(player.ContentId);
                    if (state.IsStale) status = L("FriendList.Cached") + " · " + status;
                    desiredFriends.Add(new FriendRow(player, identity, name, world, status));
                }
            }
            for (int i = 0; i < desiredFriends.Count; i++) {
                if (i >= visibleFriends.Count) visibleFriends.Add(desiredFriends[i]);
                else if (visibleFriends[i].Player != desiredFriends[i].Player || visibleFriends[i].Status != desiredFriends[i].Status || visibleFriends[i].Name != desiredFriends[i].Name || visibleFriends[i].World != desiredFriends[i].World) visibleFriends[i] = desiredFriends[i];
            }
            while (visibleFriends.Count > desiredFriends.Count) visibleFriends.RemoveAt(visibleFriends.Count - 1);
            if (selectedFriendCid != null) FriendList.SelectedItem = visibleFriends.FirstOrDefault(f => f.Player.ContentId == selectedFriendCid);
            syncing = false;
            RefreshFriendsButton.IsEnabled = App.Connection?.Available == true && state.Supported && state.RequestId == null;
            if (section == "friends") SidebarStatus.Text = state.RequestId != null ? L("FriendList.Refreshing") : snapshot != null
                ? string.Format(L("FriendList.Snapshot"), snapshot.Players.Length, snapshot.CapturedAt.ToLocalTime().ToString("MM-dd HH:mm")) + "\n" + L(state.IsStale ? "FriendList.Stale" : "FriendList.Timing")
                : L(state.Supported ? "FriendList.NotReady" : "FriendList.Unsupported");
            UpdateNavigation();
            UpdateConversationPresence();
        }
        private string PresenceText(ulong cid) {
            var state = App.Session.Friends.Presence;
            var value = state.Get(cid);
            if (state.IsPending(cid)) return L("Presence.Checking");
            if (value == null) return L("FriendList.Unknown");
            if (value.Status != FriendListStatus.Success) return L("FriendList.Unknown") + " · " + L(value.Status == FriendListStatus.TimedOut ? "Presence.Timeout" : "Presence.Unavailable");
            var label = L(value.Presence == PresenceState.Online ? "FriendList.Online" : value.Presence == PresenceState.Offline ? "FriendList.Offline" : "FriendList.Unknown");
            return label + " · " + (FriendPresenceSession.Fresh(value, DateTime.UtcNow) ? value.CheckedAt.ToLocalTime().ToString("HH:mm:ss") : L("Presence.Expired"));
        }
        private Player? ConversationFriend() {
            var state = App.Session.Friends;
            if (selectedConversation == null || string.IsNullOrEmpty(state.OwnerEpoch) || state.OwnerKey != App.Workbench.OwnerKey || state.Source != App.Workbench.Source) return null;
            return state.Snapshot?.Players.FirstOrDefault(p => !p.IdentityUnavailable && p.HomeWorld == selectedConversation.Peer.HomeWorldId &&
                string.Equals(p.Name, selectedConversation.Peer.Name, StringComparison.OrdinalIgnoreCase));
        }
        private void UpdateConversationPresence() {
            var friend = ConversationFriend();
            ChatPresenceText.Visibility = V(friend != null);
            ChatPresenceText.Text = friend == null ? "" : PresenceText(friend.ContentId);
        }
        private void PresenceTick(object? sender, object e) {
            App.Session.Friends.Presence.Expire(DateTime.UtcNow);
            UpdateConversationPresence();
            if (!active || App.Connection?.Available != true) return;
            if (ConversationFriend() is { } peer && App.Connection.RefreshFriendPresence(peer.ContentId)) return;
            if (section != "friends" || App.Session.Friends.IsStale) return;
            foreach (var row in visibleFriends) {
                if (row.Peer == null || FriendList.ContainerFromItem(row) is not FrameworkElement container) continue;
                var position = container.TransformToVisual(FriendList).TransformPoint(new Windows.Foundation.Point());
                if (position.Y + container.ActualHeight <= 0 || position.Y >= FriendList.ActualHeight) continue;
                if (App.Connection.RefreshFriendPresence(row.Player.ContentId)) return;
            }
            RefreshFriendRows();
        }
        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (initialized && !syncing && ChannelList.SelectedItem is Tab tab) SelectChannel(tab); }
        internal void SelectChannel(Tab tab) {
            SaveComposer(); UnselectConversation(); selectedChannel = tab;
            ChatPresenceText.Visibility = Visibility.Collapsed;
            ChatPanel.Visibility = Visibility.Visible; HistoryPanel.Visibility = Visibility.Collapsed; ComposerPanel.Visibility = Visibility.Visible;
            if (!channelViews.TryGetValue(tab, out var view)) { view = new Controls.ChatMessageList(tab); channelViews.Add(tab, view); }
            ChatHost.Content = view; ChatTitle.Text = App.Presentation.Text(tab.Name); ChatSubtitle.Text = L("Workbench.ChannelView");
            activeChannelDraftKey = ChannelDraftKey(tab);
            syncing = true; Composer.Text = channelDrafts.GetValueOrDefault(activeChannelDraftKey, ""); syncing = false;
            UpdateChatActions(false); UpdateReady();
        }
        private string ChannelDraftKey(Tab tab) => App.Session.Source + "/" + App.Session.Player?.Identity?.Key + "/" + tab.Id;
        private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!syncing && ConversationList.SelectedItem is ConversationModel model) ShowConversation(model); }
        private void FriendList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (syncing || FriendList.SelectedItem is not FriendRow friend) return;
            if (friend.Peer == null || !TellTarget.From(friend.Peer).IsValid) { FooterStatus.Text = L("FriendList.IdentityUnavailable"); return; }
            var model = App.Workbench.Open(friend.Peer); if (model != null) ShowConversation(model);
        }
        internal void ShowConversation(ConversationModel model) {
            if (ReferenceEquals(selectedConversation, model) && conversationView != null) { ChatHost.Content = conversationView; UpdateReady(); UpdateConversationPresence(); if (ConversationFriend() is { } current) App.Connection?.RefreshFriendPresence(current.ContentId); return; }
            SaveComposer(); UnselectConversation(); selectedConversation = model; model.PropertyChanged += SelectedConversationChanged;
            conversationTab = new Tab(model.Name) { Filter = new EverythingFilter() };
            conversationView = new Controls.ChatMessageList(conversationTab); conversationView.ReadingChanged += ReadCurrent;
            ChatHost.Content = conversationView; ChatPanel.Visibility = Visibility.Visible; HistoryPanel.Visibility = Visibility.Collapsed; ComposerPanel.Visibility = Visibility.Visible;
            ChatTitle.Text = model.Name; ChatSubtitle.Text = model.World + (model.Pinned ? " · " + L("Conversation.Pinned") : "");
            PeerAvatar.Identity = model.Peer;
            UpdateConversationPresence();
            if (ConversationFriend() is { } friend) App.Connection?.RefreshFriendPresence(friend.ContentId);
            syncing = true; Composer.Text = model.Draft; syncing = false;
            UpdateChatActions(true); UpdateReady();
            _ = LoadConversationAsync(false);
        }
        private void UnselectConversation() {
            conversationLoad?.Cancel(); conversationLoad?.Dispose(); conversationLoad = null;
            if (selectedConversation != null) selectedConversation.PropertyChanged -= SelectedConversationChanged;
            selectedConversation = null; conversationView?.Dispose(); conversationView = null; conversationTab = null; conversationSeen.Clear(); conversationOldest = null;
        }
        private void SelectedConversationChanged(object? sender, PropertyChangedEventArgs e) {
            if (selectedConversation == null) return;
            if (e.PropertyName == nameof(ConversationModel.Draft) && Composer.Text != selectedConversation.Draft) { syncing = true; Composer.Text = selectedConversation.Draft; syncing = false; }
            UpdateReady();
        }
        private async Task LoadConversationAsync(bool older) {
            var model = selectedConversation; var tab = conversationTab;
            if (model == null || tab == null) return;
            conversationLoad?.Cancel(); conversationLoad?.Dispose(); conversationLoad = new CancellationTokenSource();
            var token = conversationLoad.Token; Busy.IsActive = true;
            try {
                if (App.Session.Store is { } store) {
                    var query = new HistoryQuery(Source: model.State.Source, OwnerKey: model.State.OwnerKey, PeerKey: model.Key, Limit: 100,
                        BeforeRow: older ? conversationOldest?.RowId ?? long.MaxValue : long.MaxValue,
                        BeforeTimestampUtc: older ? conversationOldest?.Message.Timestamp : null);
                    var rows = await store.SearchAsync(query, token);
                    if (token.IsCancellationRequested || selectedConversation != model) return;
                    foreach (var row in rows.Reverse()) MergeConversation(row.Message, false);
                    if (rows.Count > 0) conversationOldest = rows[^1];
                    LoadOlderButton.IsEnabled = rows.Count == 100;
                } else LoadOlderButton.IsEnabled = false;
                if (!older && App.Session.Source == model.State.Source)
                    foreach (var message in Messages.Where(m => m.Owner?.Key == model.State.OwnerKey && ConversationIdentity.IsTell((ushort)m.Channel) && ConversationIdentity.PeerKey(m.TellPeer) == model.Key)) MergeConversation(message, false);
                ReadCurrent();
            } catch (OperationCanceledException) { }
            catch (Exception ex) { FooterStatus.Text = L("History.Unavailable") + " " + ex.Message; }
            finally { if (!token.IsCancellationRequested) Busy.IsActive = false; }
        }
        private void MergeConversation(ServerMessage message, bool live) {
            if (conversationTab == null) return;
            if (message.MessageId != null && !conversationSeen.Add(message.MessageId)) return;
            if (live) conversationTab.AddMessage(message, App.Config); else conversationTab.MergeHistory(new[] { message }, App.Config);
        }
        private void SessionMessagesChanged(ServerMessage[] messages, bool live) {
            var model = selectedConversation;
            if (model == null || model.State.Source != App.Session.Source) return;
            foreach (var message in messages.Where(m => m.Owner?.Key == model.State.OwnerKey && ConversationIdentity.IsTell((ushort)m.Channel) && ConversationIdentity.PeerKey(m.TellPeer) == model.Key)) MergeConversation(message, live);
            ReadCurrent();
        }
        private void ReadCurrent() {
            if (active && section == "events") _ = MarkVisibleEventsReadAsync();
            if (active && ChatPanel.Visibility == Visibility.Visible && selectedConversation?.Unread > 0 && conversationView?.FollowingLatest == true) _ = App.Workbench.MarkReadAsync(selectedConversation);
        }
        private void ShowEmpty() {
            ChatHost.Content = new TextBlock { Text = L(section == "friends" ? "FriendList.Select" : "Conversation.Select"), TextWrapping = TextWrapping.Wrap, Opacity = .6, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ChatTitle.Text = L("Workbench." + (section == "friends" ? "Friends" : "Conversations")); ChatSubtitle.Text = ""; ComposerPanel.Visibility = Visibility.Collapsed;
            PopoutButton.IsEnabled = false;
            UpdateChatActions(false); EditChannelButton.Visibility = Visibility.Collapsed;
        }
        private void UpdateChatActions(bool conversation) {
            PopoutButton.IsEnabled = section is "channels" or "conversations" or "friends";
            PeerAvatar.Visibility = V(conversation); PinConversationButton.Visibility = V(conversation); ConversationNoteButton.Visibility = V(conversation); NicknameButton.Visibility = V(conversation);
            AvatarButton.Visibility = V(conversation); LoadOlderButton.Visibility = V(conversation); EditChannelButton.Visibility = V(!conversation); BackHistoryButton.Visibility = Visibility.Collapsed;
        }
        private void Screenshot_Click(object sender, RoutedEventArgs e) => ScreenshotWindow.ShowScreenshot();
        private void UpdateReady() {
            if (!initialized) return;
            var connection = App.Connection; var model = selectedConversation; var player = App.Session.Player;
            MenuConnect.IsEnabled = !App.Connected; MenuDisconnect.IsEnabled = App.Connected; MenuRefreshFriends.IsEnabled = connection?.Available == true;
            var quickLabel = connection != null
                ? (connection.SessionReady ? SetupText.T("已连接：", "Connected: ") : SetupText.T("正在连接／等待信任：", "Connecting / awaiting trust: ")) + connection.Endpoint
                : App.Config.LastSuccessfulConnection is { } recent ? SetupText.T("连接到 ", "Connect to ") + recent.Name + " · " + recent.Description : SetupText.T("设置游戏连接", "Set up a game connection");
            ToolTipService.SetToolTip(QuickConnectButton, quickLabel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QuickConnectButton, quickLabel);
            ConnectionLabel.Text = connection?.Available == true ? L("Workbench.Connected") : L("Workbench.Disconnected");
            OwnAvatar.Identity = player?.Identity ?? App.Workbench.Owner;
            UpdatePlayerDisplay();
            Composer.PlaceholderText = L("Workbench.TypeMessage");
            Composer.IsEnabled = model != null || connection?.Available == true;
            SymbolPicker.IsEnabled = Composer.IsEnabled;
            var canTell = model != null && model.State.Source == App.Session.Source && model.State.OwnerKey == player?.Identity?.Key && connection?.Available == true && connection.SupportsDirectedTell;
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(Composer.Text) && (model != null ? canTell : connection?.Available == true && selectedChannel != null);
            ChannelSwitchButton.Visibility = V(model == null); ChannelSwitchButton.Content = connection?.CurrentChannel ?? L("Workbench.Channel"); ChannelSwitchButton.IsEnabled = connection?.Available == true;
            ComposerTarget.Text = model != null ? string.Format(L(model.World.Length == 0 ? "Conversation.TargetPrivate" : "Conversation.Target"), model.Name, model.World) : L("Workbench.ChannelTarget");
            ComposerStatus.Text = model != null && !canTell
                ? L(connection?.Available == true && !connection.SupportsDirectedTell ? "Conversation.UpgradeRequired" : "Conversation.ReadOnly")
                : model?.SendStatus is { Length: > 0 } status ? status : L("Workbench.EnterHint");
            RestoreDraftButton.Visibility = V(model?.FailedDraft != null); PinConversationButton.IsChecked = model?.Pinned == true;
            FooterStatus.Text = App.Presentation.Error != null ? L("Privacy.SaveFailed") : App.Workbench.Error is { } error ? L("History.Unavailable") + " " + error :
                (App.Config.HistoryEnabled ? string.Format(L("Workbench.HistoryRetention"), App.Config.HistoryRetentionDays == 0 ? L("Workbench.Forever") : App.Config.HistoryRetentionDays.ToString()) : L("Workbench.HistoryOff"));
        }
        private void SaveComposer() {
            if (!initialized || syncing) return;
            if (selectedConversation != null) { if (selectedConversation.Draft != Composer.Text) selectedConversation.Draft = Composer.Text; if (selectedConversation.Dirty) App.Workbench.Save(selectedConversation); }
            else if (activeChannelDraftKey != null && section == "channels") channelDrafts[activeChannelDraftKey] = Composer.Text;
        }
        private void Composer_TextChanging(TextBox sender, TextBoxTextChangingEventArgs e) {
            if (!initialized || syncing) return;
            if (selectedConversation != null) { selectedConversation.Draft = Composer.Text; App.Workbench.Save(selectedConversation, true); }
            UpdateReady();
        }
        private void Input_Submit(object sender, KeyRoutedEventArgs e) {
            if (e.Key != Windows.System.VirtualKey.Enter || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
            e.Handled = true; Submit();
        }
        private void Send_Click(object sender, RoutedEventArgs e) => Submit();
        private void Submit() {
            if (selectedConversation is { } conversation) {
                if (!App.Workbench.Send(conversation, Composer.Text)) ComposerStatus.Text = L("Conversation.NotSent");
            } else if (App.Connection?.SendMessage(Composer.Text) == true) Composer.Text = "";
        }
        public TextBox? GetCurrentInputBox() => ComposerPanel.Visibility == Visibility.Visible ? Composer : null;
        public void InsertTellCommand(string name, string world, bool focus = true) {
            var worldId = Enumerable.Range(1, ushort.MaxValue).Select(i => (ushort)i).FirstOrDefault(id => string.Equals(Util.WorldName(id), world, StringComparison.OrdinalIgnoreCase));
            if (worldId == 0) { FooterStatus.Text = L("FriendList.IdentityUnavailable"); return; }
            var model = App.Workbench.Open(new CharacterIdentity { Name = name, HomeWorld = world, HomeWorldId = worldId });
            if (model == null) return;
            Navigate("conversations"); ShowConversation(model); if (focus) Composer.Focus(FocusState.Programmatic);
        }
        public void ClearAllMessages() => App.Session.Clear();
        public void AddMessage(ServerMessage message) => App.Session.Add(message);
        public void AddReversedChunk(ServerMessage[] messages, int sequence) => App.Session.AddBacklog(messages, sequence);
        public void AddSystemMessage(string content) => AddMessage(new ServerMessage(DateTime.UtcNow, 0, Array.Empty<byte>(), Encoding.UTF8.GetBytes(content), new List<Chunk> { new TextChunk(content) { Foreground = 0xb38cffff } }));
        private void RefreshFriends_Click(object sender, RoutedEventArgs e) { if (App.Connection?.RefreshFriends() != true) FooterStatus.Text = L("FriendList.NotReady"); }
        private void Connect_Click(object sender, RoutedEventArgs e) => new ConnectDialog().Activate();
        private void QuickConnect_Click(object sender, RoutedEventArgs e) {
            if (App.Connection is { } active) {
                var menu = new MenuFlyout();
                menu.Items.Add(new MenuFlyoutItem { Text = active.Endpoint, IsEnabled = false });
                var stop = new MenuFlyoutItem { Text = active.SessionReady ? L("Menu.Disconnect") : SetupText.T("取消连接", "Cancel connection") };
                stop.Click += (_, _) => App.Disconnect(); menu.Items.Add(stop);
                var manage = new MenuFlyoutItem { Text = SetupText.T("管理连接", "Manage connections") };
                manage.Click += (_, _) => new ConfigWindow(App.Config).Activate(); menu.Items.Add(manage); menu.ShowAt(QuickConnectButton); return;
            }
            if (App.Config.LastSuccessfulConnection is { } recent) App.Connect(recent);
            else if (App.Config.SetupVersion == 0) SetupWizard.Show();
            else new ConnectDialog().Activate();
        }
        private void Disconnect_Click(object sender, RoutedEventArgs e) => App.Disconnect();
        private void Configuration_Click(object sender, RoutedEventArgs e) => new ConfigWindow(App.Config).Activate();
        private void EditChannel_Click(object sender, RoutedEventArgs e) { if (selectedChannel != null) new ManageTab(selectedChannel).Activate(); }
        private void Export_Click(object sender, RoutedEventArgs e) => OpenExport();
        private async void Exit_Click(object sender, RoutedEventArgs e) => await App.Workspace.ShutdownAsync();
        private void Map_Click(object sender, RoutedEventArgs e) => MapWindow.ShowMap(CurrentPlayerData?.mapId, CurrentPlayerData?.mapX, CurrentPlayerData?.mapY, LocationText.Text, CurrentPlayerData?.mapFilenameId, CurrentPlayerData?.mapSizeFactor);
        private void LocationBtn_Click(object sender, RoutedEventArgs e) => Map_Click(sender, e);
        private void LoadOlder_Click(object sender, RoutedEventArgs e) => _ = LoadConversationAsync(true);
        private void PinConversation_Click(object sender, RoutedEventArgs e) { if (selectedConversation is { } model) { model.Pinned = PinConversationButton.IsChecked == true; App.Workbench.Save(model); App.Workbench.Sort(); FilterConversations(); } }
        private void RestoreDraft_Click(object sender, RoutedEventArgs e) { if (selectedConversation?.FailedDraft is { } failed) { Composer.Text = Composer.Text.Length == 0 ? failed : Composer.Text + "\n" + failed; selectedConversation.SetStatus(""); } }
        private MenuFlyout CreateChannelFlyout() {
            var flyout = new MenuFlyout();
            foreach (var channel in Enum.GetValues<InputChannel>().Distinct()) {
                var item = new MenuFlyoutItem { Text = L("Filter." + channel) }; item.Click += (_, _) => App.Connection?.ChangeChannel(channel); flyout.Items.Add(item);
            }
            return flyout;
        }
        private void DisposeViews() {
            App.Updates.Changed -= UpdateReleaseBanner;
            eventLoadVersion++;
            App.Notifier.EventsChanged -= EventsChanged;
            App.Cards.FavoritesChanged -= FavoritesChanged; favoritesVersion++;
            presenceTimer.Stop(); presenceTimer.Tick -= PresenceTick;
            App.Session.Friends.Presence.Changed -= RefreshFriendRows;
            App.Presentation.Changed -= UpdatePrivacyDisplay;
            App.Config.Tabs.CollectionChanged -= TabsChanged; App.Config.Saved -= ConfigSaved; App.PropertyChanged -= AppChanged;
            App.Workbench.Changed -= WorkbenchChanged; App.Session.MessagesChanged -= SessionMessagesChanged; App.Session.Cleared -= SessionCleared; App.Session.Friends.Changed -= RefreshFriendRows;
            if (observedConnection != null) observedConnection.PropertyChanged -= ConnectionChanged;
            foreach (var view in channelViews.Values) view.Dispose(); channelViews.Clear(); UnselectConversation(); contextView?.Dispose(); historyCancellation?.Cancel();
        }
        private sealed class EverythingFilter : Filter { public override bool Allowed(ServerMessage message) => true; }
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void OnPropertyChanged([CallerMemberName] string? property = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property)); UpdateReady(); }
    }
}
