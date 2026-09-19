using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop;

public sealed class ChatPopoutWindow : Window {
    private readonly App app;
    private readonly WorkspaceWindows workspace;
    public ChatWindowState State { get; }
    private readonly Tab tab;
    private readonly Controls.ChatMessageList messages;
    private readonly TextBox composer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxHeight = 160, MaxLength = 16384 };
    private readonly TextBlock privacyStatus = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private XIVChatCommon.Presentation.DisplayContext DisplayContext => app.Presentation.Context(State.Source, State.OwnerKey, State.Owner);
    private readonly TextBlock target = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = .8 };
    private readonly TextBlock historyStatus = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private readonly Button send = new();
    private readonly Controls.GameSymbolPicker symbols = new();
    private readonly Button older = new();
    private readonly Button returnMain = new();
    private readonly Button restoreDraft = new();
    private readonly Button menu = new() { Content = "⋯" };
    private readonly HashSet<string> seen = new();
    private readonly CancellationTokenSource lifetime = new();
    private ConversationModel? model;
    private Connection? observed;
    private HistoryRow? oldest;
    private bool active, closed, allowClose, loading, ready, historyLoaded, initializing = true;
    private string sendStatus = "";
    private string historyStateKey = "";
    private string? historyDetail;
    private static string L(string key) => LocalizationHelper.GetString(key);
    public TextBox Composer => composer;
    internal Controls.ChatMessageList MessageView => messages;
    internal IReadOnlyList<ServerMessage> LoadedMessages => tab.Messages;
    private Tab? Channel => app.Config.Tabs.FirstOrDefault(t => t.Id == State.ChannelId);
    private bool ContextMatches => State.Source == app.Session.Source && State.OwnerKey == (app.Session.Player?.Identity?.Key ?? "unassigned:" + app.Session.Source);
    public bool CanSend => ContextMatches && app.Connection?.Available == true &&
        (State.Peer != null ? app.Connection.SupportsDirectedTell && model != null : Channel != null);
    public HistoryQuery ExportQuery => new(Source: State.Source, OwnerKey: State.OwnerKey, PeerKey: ConversationIdentity.PeerKey(State.Peer));

    public ChatPopoutWindow(App app, WorkspaceWindows workspace, ChatWindowState state) {
        this.app = app; this.workspace = workspace; State = state;
        tab = new(state.Name) { Filter = new ViewFilter(this), ProcessMarkdown = state.Markdown, ShowTimestamps = state.Timestamps, FontSizeOverride = state.FontSize };
        messages = new(tab); composer.Text = state.Draft;
        var root = ThemeHelper.CreateSurface(); root.Padding = new Thickness(12); root.RowSpacing = 8;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(returnMain); toolbar.Children.Add(older); toolbar.Children.Add(menu); root.Children.Add(toolbar);
        var heading = new StackPanel { Spacing = 4 }; heading.Children.Add(privacyStatus); heading.Children.Add(target); heading.Children.Add(historyStatus);
        Grid.SetRow(heading, 1); root.Children.Add(heading); Grid.SetRow(messages, 2); root.Children.Add(messages);
        var input = new StackPanel { Spacing = 6 }; input.Children.Add(composer); input.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        symbols.Attach(composer); actions.Children.Add(restoreDraft); actions.Children.Add(symbols); actions.Children.Add(send); input.Children.Add(actions); Grid.SetRow(input, 3); root.Children.Add(input);
        Content = root; ThemeHelper.InitializeWindow(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = state.Topmost;
        AppWindow.Changed += (_, e) => { if (e.DidPositionChange || e.DidSizeChange) workspace.ScheduleSave(); };
        AppWindow.Closing += async (_, e) => { if (allowClose) return; e.Cancel = true; await workspace.ClosePopoutAsync(this); };
        Closed += (_, _) => Dispose();
        Activated += (_, e) => { active = e.WindowActivationState != WindowActivationState.Deactivated; MarkRead(); };
        returnMain.Click += (_, _) => { workspace.ShowMain(); app.Window.ReturnPopout(State); };
        older.Click += async (_, _) => await LoadAsync(true);
        send.Click += (_, _) => Submit();
        composer.TextChanging += (_, _) => { if (initializing) return; State.Draft = composer.Text; UpdateReady(); workspace.ScheduleSave(); };
        composer.KeyDown += (_, e) => {
            if (e.Key != Windows.System.VirtualKey.Enter || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
            e.Handled = true; Submit();
        };
        restoreDraft.Click += (_, _) => {
            if (State.FailedDraft is not { } failed) return;
            if (composer.Text.Length + failed.Length + 1 > composer.MaxLength) { status.Text = L("Windows.DraftFull"); return; }
            composer.Text += (composer.Text.Length == 0 ? "" : "\n") + failed; State.FailedDraft = null; UpdateReady(); workspace.ScheduleSave();
        };
        messages.ReadingChanged += ReadingChanged;
        app.Session.MessagesChanged += MessagesChanged;
        app.Workbench.Changed += ContextChanged;
        app.PropertyChanged += AppChanged;
        app.Config.Saved += ConfigChanged;
        app.Presentation.Changed += Localize;
        LocalizationHelper.LanguageChanged += Localize;
        root.Loaded += async (_, _) => {
            if (ready) return; ready = true;
            if (root.XamlRoot != null) {
                var scale = root.XamlRoot.RasterizationScale;
                root.XamlRoot.Changed += (_, _) => {
                    if (closed || root.XamlRoot.RasterizationScale == scale) return;
                    scale = root.XamlRoot.RasterizationScale; WorkspaceWindows.ApplyBounds(this, WorkspaceWindows.BoundsOf(this));
                };
            }
            await LoadAsync(false);
        };
        initializing = false; ObserveConnection(); ContextChanged(); Localize();
        if (State.Source == app.Session.Source) AddMessages(app.Session.Messages);
    }
    private void Localize() {
        if (closed) return;
        Title = (State.Peer == null ? app.Presentation.Text(State.Name, DisplayContext) : app.Presentation.Identity(State.Peer, DisplayContext).Name) + " · XIVChat";
        privacyStatus.Text = L("Privacy.Active"); privacyStatus.Visibility = app.Presentation.Enabled ? Visibility.Visible : Visibility.Collapsed;
        returnMain.Content = L("Windows.ReturnMain"); older.Content = L("History.LoadOlder"); send.Content = L("Workbench.Send");
        restoreDraft.Content = L("Conversation.RestoreDraft"); composer.PlaceholderText = L("Workbench.TypeMessage");
        symbols.Localize();
        menu.Flyout = BuildMenu(); messages.UpdateLocalizations(); UpdateReady();
        if (historyStateKey.Length > 0) SetHistoryStatus(historyStateKey, historyDetail);
    }
    private void SetHistoryStatus(string key, string? detail = null) {
        historyStateKey = key; historyDetail = detail; historyStatus.Text = L(key) + (detail == null ? "" : " " + detail);
    }
    private MenuFlyout BuildMenu() {
        var flyout = new MenuFlyout();
        void Toggle(string name, bool value, Action<bool> change) {
            var item = new ToggleMenuFlyoutItem { Text = L(name), IsChecked = value };
            item.Click += (_, _) => { change(item.IsChecked); workspace.ScheduleSave(); }; flyout.Items.Add(item);
        }
        Toggle("Windows.Topmost", State.Topmost, value => { State.Topmost = value; if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = value; });
        Toggle("Export.ShowTimestamps", State.Timestamps, value => tab.ShowTimestamps = State.Timestamps = value);
        Toggle("ManageTab.ProcessMarkdown", State.Markdown, value => tab.ProcessMarkdown = State.Markdown = value);
        var fonts = new MenuFlyoutSubItem { Text = L("Windows.FontSize") };
        foreach (var size in new[] { 10d, 12, 14, 16, 18, 20, 24, 28, 32 }) {
            var item = new MenuFlyoutItem { Text = size.ToString() };
            item.Click += (_, _) => { tab.FontSizeOverride = State.FontSize = size; workspace.ScheduleSave(); };
            fonts.Items.Add(item);
        }
        flyout.Items.Add(fonts);
        if (State.ChannelId != null) {
            var channels = new MenuFlyoutSubItem { Text = L("Workbench.Channel") };
            foreach (var channel in Enum.GetValues<InputChannel>().Distinct()) {
                var item = new MenuFlyoutItem { Text = L("Filter." + channel) };
                item.Click += (_, _) => { if (CanSend) app.Connection?.ChangeChannel(channel); }; channels.Items.Add(item);
            }
            flyout.Items.Add(channels);
        }
        var export = new MenuFlyoutItem { Text = L("Menu.Export") };
        export.Click += (_, _) => new Export(ExportQuery, State.Name, SnapshotChannelFilter()).Activate(); flyout.Items.Add(export);
        var exit = new MenuFlyoutItem { Text = L("Menu.Exit") };
        exit.Click += async (_, _) => await workspace.ShutdownAsync(); flyout.Items.Add(exit);
        return flyout;
    }
    private Filter? SnapshotChannelFilter() => State.ChannelId == null ? null : new Filter { Types = Channel?.Filter.Types.ToHashSet() ?? new() };
    private void AppChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(App.Connection)) ObserveConnection(); }
    private void ObserveConnection() {
        if (observed != null) observed.PropertyChanged -= ConnectionChanged;
        observed = app.Connection; if (observed != null) observed.PropertyChanged += ConnectionChanged;
        UpdateReady();
    }
    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e) => app.Dispatch(UpdateReady);
    private void ConfigChanged() {
        var anchor = messages.CaptureScrollId(); var following = messages.FollowingLatest;
        tab.RepopulateMessages(tab.Messages.ToArray());
        if (!messages.RestoreScroll(anchor, following)) SetHistoryStatus("Windows.AnchorUnavailable");
        Localize();
    }
    private void ContextChanged() {
        var current = app.Workbench.Source == State.Source && app.Workbench.OwnerKey == State.OwnerKey
            ? app.Workbench.Conversations.FirstOrDefault(c => c.Key == ConversationIdentity.PeerKey(State.Peer)) : null;
        if (!ReferenceEquals(model, current)) {
            if (model != null) model.PropertyChanged -= ModelChanged;
            model = current; if (model != null) model.PropertyChanged += ModelChanged;
        }
        UpdateReady(); MarkRead();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => UpdateReady();
    private void UpdateReady() {
        if (closed) return;
        var peerDisplay = app.Presentation.Identity(State.Peer, DisplayContext);
        target.Text = State.Peer != null ? string.Format(L(peerDisplay.World.Length == 0 ? "Conversation.TargetPrivate" : "Conversation.Target"), peerDisplay.Name, peerDisplay.World)
            : L("Workbench.ChannelTarget") + " · " + (app.Connection?.CurrentChannel ?? L("Status.Disconnected"));
        target.Text += "\n" + (State.Owner is { } owner ? app.Presentation.Identity(owner, DisplayContext).Label : L("History.Unassigned"));
        send.IsEnabled = CanSend && State.PendingDraft == null && State.FailedDraft == null && !string.IsNullOrWhiteSpace(composer.Text);
        status.Text = !CanSend ? L(State.ChannelId != null && Channel == null ? "Windows.ViewMissing" : "Conversation.ReadOnly") : sendStatus.Length > 0 ? sendStatus : L("Workbench.EnterHint");
        restoreDraft.Visibility = State.FailedDraft == null ? Visibility.Collapsed : Visibility.Visible;
    }
    public bool Submit() {
        if (!CanSend || State.PendingDraft != null || State.FailedDraft != null || string.IsNullOrWhiteSpace(composer.Text)) { sendStatus = L("Conversation.NotSent"); UpdateReady(); return false; }
        var text = composer.Text;
        State.PendingDraft = text;
        var key = State.Key;
        void Reply(string reply, string? failed, bool complete) => workspace.SendReply(key, reply, failed, complete);
        bool accepted = State.Peer != null ? app.Workbench.Send(model!, text, Reply)
            : app.Workbench.SendChannel(State.Source, State.OwnerKey, text, Reply);
        if (accepted) composer.Text = "";
        else { State.PendingDraft = null; sendStatus = L("Conversation.NotSent"); }
        workspace.ScheduleSave();
        UpdateReady(); return accepted;
    }
    internal void ShowWorkspaceError(string error) => SetHistoryStatus("Windows.SaveFailed", error);
    internal void ShowSendReply(string reply) {
        sendStatus = reply;
        if (composer.Text != State.Draft) composer.Text = State.Draft;
        UpdateReady();
    }
    private bool Accepts(ServerMessage message) => (message.Owner?.Key ?? "unassigned:" + State.Source) == State.OwnerKey &&
        (State.Peer != null ? ConversationIdentity.IsTell((ushort)message.Channel) && ConversationIdentity.PeerKey(message.TellPeer) == ConversationIdentity.PeerKey(State.Peer)
            : Channel?.Filter.Allowed(message) == true);
    private string MessageKey(ServerMessage message) => message.LocalStorageId ??
        (message.MessageId == null ? "memory:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(message) : HistoryStore.StorageId(State.Source, message));
    private void AddMessages(IEnumerable<ServerMessage> batch) {
        var accepted = batch.Where(Accepts).Where(m => seen.Add(MessageKey(m))).ToArray();
        if (accepted.Length > 0) tab.MergeHistory(accepted, app.Config);
        if (seen.Count > Math.Max(1000, app.Config.LocalBacklogMessages * 2)) {
            seen.Clear(); foreach (var message in tab.Messages) seen.Add(MessageKey(message));
        }
    }
    private void MessagesChanged(ServerMessage[] batch, bool live) {
        if (closed || State.Source != app.Session.Source) return;
        AddMessages(batch); MarkRead();
    }
    private void ReadingChanged() { if (!initializing) workspace.ScheduleSave(); MarkRead(); }
    private void MarkRead() {
        if (!closed && active && historyLoaded && !loading && !initializing && messages.FollowingLatest && model is { Unread: > 0 }) _ = app.Workbench.MarkReadAsync(model);
    }
    private async Task LoadAsync(bool more) {
        if (loading || closed) return;
        if (app.Session.Store is not { } store) { SetHistoryStatus("History.Unavailable"); historyLoaded = true; return; }
        loading = true; older.IsEnabled = false;
        var scroll = State.ScrollId; var following = State.FollowingLatest;
        try {
            var query = ExportQuery with { Limit = 500 };
            if (more && oldest != null) query = query with { BeforeRow = oldest.RowId, BeforeTimestampUtc = oldest.Message.Timestamp };
            var rows = await store.SearchAsync(query, lifetime.Token);
            if (closed) return;
            AddMessages(rows.Reverse().Select(r => r.Message)); oldest = rows.LastOrDefault() ?? oldest;
            older.IsEnabled = rows.Count == 500;
            if (!more && !following && scroll != null) {
                if (tab.Messages.All(m => m.LocalStorageId != scroll)) {
                    var context = await store.GetContextAsync(scroll, 100, lifetime.Token);
                    if (closed) return;
                    AddMessages(context.Where(r => r.Source == State.Source && r.OwnerKey == State.OwnerKey).Select(r => r.Message));
                }
                if (!messages.RestoreScroll(scroll, false)) SetHistoryStatus("Windows.AnchorUnavailable");
            }
            if (historyStatus.Text.Length == 0) SetHistoryStatus(app.Config.HistoryEnabled ? "Windows.StoredOnly" : "Workbench.HistoryOff");
        } catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) { SetHistoryStatus("History.Unavailable", ex.Message); older.IsEnabled = true; } }
        finally { loading = false; historyLoaded = true; if (!closed) MarkRead(); }
    }
    public void Capture() {
        if (closed) return;
        State.Draft = composer.Text;
        if (WorkspaceWindows.IsNormal(this)) State.Bounds = WorkspaceWindows.BoundsOf(this);
        // Do not overwrite a stored reading anchor before its asynchronous history load finishes.
        if (ready && !loading) { State.FollowingLatest = messages.FollowingLatest; State.ScrollId = messages.CaptureScrollId(); }
    }
    public void BringForward() {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }
    public bool Matches(NotificationTarget target) => target.Kind != NotificationTargetKind.Event && target.Source == State.Source && target.OwnerKey == State.OwnerKey &&
        (State.Peer != null ? ConversationIdentity.PeerKey(target.Peer) == ConversationIdentity.PeerKey(State.Peer)
            : target.Channel is { } channel && Channel?.Filter.Types.Any(t => t.Allowed(new ChatCode(channel))) == true);
    public bool IsReading(NotificationTarget target) => !closed && active && historyLoaded && !loading && messages.FollowingLatest && Matches(target);
    public async Task OpenNotificationAsync(NotificationTarget target) {
        BringForward();
        if (target.RecordId != null && app.Session.Store is { } store) {
            try {
                var context = await store.GetContextAsync(target.RecordId, 20, lifetime.Token);
                if (closed) return;
                AddMessages(context.Where(r => r.Source == State.Source && r.OwnerKey == State.OwnerKey).Select(r => r.Message));
                var message = tab.Messages.FirstOrDefault(m => m.LocalStorageId == target.RecordId);
                if (message != null) messages.ScrollToMessage(message); else SetHistoryStatus("Notify.TargetMissing");
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) SetHistoryStatus("Notify.OpenFailed", ex.Message); }
        }
    }
    public new void Close() => _ = workspace.ClosePopoutAsync(this);
    internal void CloseForWorkspace() { allowClose = true; base.Close(); }
    private void Dispose() {
        if (closed) return;
        closed = true; lifetime.Cancel(); lifetime.Dispose(); messages.Dispose();
        if (model != null) model.PropertyChanged -= ModelChanged;
        if (observed != null) observed.PropertyChanged -= ConnectionChanged;
        app.Session.MessagesChanged -= MessagesChanged; app.Workbench.Changed -= ContextChanged;
        app.Presentation.Changed -= Localize;
        app.PropertyChanged -= AppChanged; app.Config.Saved -= ConfigChanged; LocalizationHelper.LanguageChanged -= Localize;
    }
    private sealed class ViewFilter(ChatPopoutWindow window) : Filter {
        public override bool Allowed(ServerMessage message) => window.Accepts(message);
    }
}
