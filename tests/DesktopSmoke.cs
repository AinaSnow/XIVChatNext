using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Sodium;
using XIVChatCommon;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChat_Desktop.Controls;

namespace XIVChat_Desktop {
    // Compiled only when DesktopSmoke.targets is explicitly supplied to MSBuild.
    internal static class DesktopSmokeProgram {
        [STAThread]
        public static void Main() {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters => {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new DesktopSmokeApp();
            });
        }
    }

    internal sealed partial class DesktopSmokeApp : App {
        private readonly List<string> results = new();
        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            // Do not run App.OnLaunched: the smoke test must not read or save the user's configuration.
            try {
                var first = new Tab("General") { Filter = Tab.GeneralFilter() };
                var second = new Tab("Also general") { Filter = Tab.GeneralFilter() };
                var config = new Configuration { FilePathOverride = Path.Combine(AppContext.BaseDirectory, "smoke-config.json"), OnlineAvatars = false, LocalBacklogMessages = 10_000, BacklogMessages = 0, Tabs = new ObservableCollection<Tab> { first, second } };
                typeof(App).GetProperty(nameof(Config))!.SetValue(this, config);
                this.Notifier.Sink = notificationSink;
                var legacy = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(config));
                legacy.Remove("OnlineAvatars");
                foreach (var tab in legacy["Tabs"]!.Children<Newtonsoft.Json.Linq.JObject>()) tab.Remove("Id");
                var migrated = (Configuration)typeof(Configuration).GetMethod("Deserialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { legacy.ToString() })!;
                this.Check(migrated.Tabs.Count == 2 && migrated.Tabs.Select(t => t.Name).SequenceEqual(new[] { "General", "Also general" }) && migrated.Tabs.Select(t => t.Id).Distinct().Count() == 2 && migrated.Tabs[0].Filter.Types.SetEquals(first.Filter.Types),
                    "Legacy channel names, filters and order survive generated view IDs");
                LocalizationHelper.Initialize(AppLanguage.English);
                foreach (var light in new[] { true, false }) {
                    var adjusted = new[] { Microsoft.UI.Colors.White, Microsoft.UI.Colors.Black, Windows.UI.Color.FromArgb(255, 255, 180, 220) }
                        .Select(c => MessageFormatter.ReadableColor(c, light));
                    double Linear(byte b) => b / 255d <= .04045 ? b / 255d / 12.92 : Math.Pow((b / 255d + .055) / 1.055, 2.4);
                    double Lum(Windows.UI.Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
                    var bg = light ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32);
                    this.Check(adjusted.All(c => (Math.Max(Lum(c), Lum(bg)) + .05) / (Math.Min(Lum(c), Lum(bg)) + .05) >= 4.5), "Chat colors meet readable contrast in " + (light ? "light" : "dark") + " theme");
                }
                var window = new MainWindow();
                typeof(App).GetProperty(nameof(Window))!.SetValue(this, window);
                window.Activate();
                await Task.Delay(300);

                var normalSize = window.AppWindow.Size;
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 540));
                await Task.Delay(250);
                var rail = Find<ScrollViewer>((DependencyObject)window.Content, v => v.Name == "NavigationRailScroll");
                var eventsButton = Find<Button>((DependencyObject)window.Content, b => b.Name == "NavEvents");
                var settingsButton = Find<Button>((DependencyObject)window.Content, b => b.Name == "SettingsButton");
                Check(rail.ScrollableHeight > 0, "Short windows keep the navigation rail scrollable");
                rail.ChangeView(null, rail.ScrollableHeight, null, true);
                await Task.Delay(150);
                var eventsTop = eventsButton.TransformToVisual(rail).TransformPoint(new Windows.Foundation.Point()).Y;
                Check(eventsTop >= -1 && eventsTop + eventsButton.ActualHeight <= rail.ActualHeight + 1,
                    "Events navigation is fully reachable in a 540-pixel window");
                var railBottom = rail.TransformToVisual((UIElement)window.Content).TransformPoint(new Windows.Foundation.Point(0, rail.ActualHeight)).Y;
                var settingsTop = settingsButton.TransformToVisual((UIElement)window.Content).TransformPoint(new Windows.Foundation.Point()).Y;
                Check(railBottom <= settingsTop + 1, "Scrolled navigation does not overlap the pinned settings button");
                window.AppWindow.Resize(normalSize);
                rail.ChangeView(null, 0, null, true);
                await Task.Delay(150);

                var editServer = new ManageServer(null);
                var editView = new ManageTab(first);
                editServer.Activate(); editView.Activate();
                await Task.Delay(250);
                var serverName = Find<TextBox>((DependencyObject)editServer.Content, c => c.Name == "ServerName");
                serverName.Text = "Unsaved server name";
                var sayOption = Descendants<CheckBox>((DependencyObject)editView.Content).First(c => Localize.GetContent(c) == "Filter.Say");
                sayOption.IsChecked = true;
                Check(editServer.Title == "Add server" && sayOption.Content?.ToString() == "Say", "English resources reach legacy dialogs and generated filters");
                LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified);
                await Task.Delay(100);
                Check(editServer.Title == "添加服务器" && sayOption.Content?.ToString() == "说话" && Find<Button>((DependencyObject)editServer.Content, b => Localize.GetContent(b) == "Dialog.Save").Content?.ToString() == "保存", "Open dialog titles, XAML bindings and generated options switch to Chinese");
                Check(serverName.Text == "Unsaved server name" && sayOption.IsChecked == true, "Language switching preserves unsaved input and filter selection");
                Check(Enum.GetValues<FilterType>().All(t => LocalizationHelper.GetString("Filter." + t) != "Filter." + t) && Enum.GetValues<ChatType>().All(t => LocalizationHelper.GetString("ChatType." + t) != "ChatType." + t), "Packaged Chinese resources cover every filter and chat channel");
                LocalizationHelper.ApplyLanguage(AppLanguage.English);
                await Task.Delay(100);
                Check(editServer.Title == "Add server" && sayOption.Content?.ToString() == "Say", "Existing dialogs switch back to English");
                editServer.Close(); editView.Close(); window.Activate();

                for (int i = 0; i < 10_000; i++) window.AddMessage(Message(i));
                await Task.Delay(1500);
                var tabs = Find<ListView>((DependencyObject)window.Content, v => v.Name == "ChannelList");
                var firstView = Find<ChatMessageList>((DependencyObject)window.Content);
                var list = Find<ListView>(firstView);
                var viewer = Find<ScrollViewer>(list);
                Check(list.Items.Count == 10_000 && second.Messages.Count == 10_000, "10,000 messages retained in both tabs");
                var realized = Descendants<MessageTextBlock>(list).Count();
                Check(realized > 0 && realized < 200, $"Virtualized message controls: {realized}");
                Check(viewer.ScrollableHeight - viewer.VerticalOffset < 5, "Initial backlog follows latest");

                viewer.ChangeView(null, viewer.ScrollableHeight / 2, null, true);
                await Task.Delay(500);
                var anchor = FirstVisible(list);
                var button = Find<Button>(firstView, b => b.Name == "LatestButton");
                Check(button.Visibility == Visibility.Visible, "Scrolling up reveals return-to-latest");
                for (int i = 10_000; i < 10_100; i++) window.AddMessage(Message(i));
                await Task.Delay(700);
                Check(ReferenceEquals(FirstVisible(list), anchor), "Appending and pruning preserve the visible message");
                Check(button.Content.ToString()!.Contains("100"), "New message count displayed while reading history");

                tabs.SelectedIndex = 1;
                await Task.Delay(300);
                tabs.SelectedIndex = 0;
                await Task.Delay(300);
                Check(ReferenceEquals(FirstVisible(list), anchor), "Switching tabs preserves reading position");
                var third = new Tab("Temporary");
                config.Tabs.Add(third);
                await Task.Delay(200);
                config.Tabs.Remove(third);
                await Task.Delay(200);
                Check(ReferenceEquals(FirstVisible(list), anchor), "Adding/removing another tab preserves reading position");

                var peer = new ButtonAutomationPeer(button);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(500);
                Check(button.Visibility == Visibility.Collapsed && viewer.ScrollableHeight - viewer.VerticalOffset < 5, $"Return-to-latest clears unread count and scrolls down (button={button.Visibility}, offset={viewer.VerticalOffset}, extent={viewer.ScrollableHeight})");
                window.AddMessage(Message(10_100));
                await Task.Delay(300);
                Check(viewer.ScrollableHeight - viewer.VerticalOffset < 5, "Subsequent messages continue following latest");

                window.ClearAllMessages();
                await Task.Delay(100);
                Check(list.Items.Count == 0, "Clearing messages resets the virtualized list");
                // Exercise indexed backlog batches while pruning at a small retention limit.
                config.LocalBacklogMessages = 5;
                window.AddMessage(Message(0));
                window.AddMessage(Message(1));
                window.AddMessage(Message(2));
                window.AddReversedChunk(new[] { Message(6), Message(7), Message(8) }, 42);
                window.AddReversedChunk(new[] { Message(3), Message(4), Message(5) }, 42);
                await Task.Delay(100);
                Check(window.Messages.Select(msg => msg.ContentText.Split(':')[0]).SequenceEqual(new[] { "Message 4", "Message 5", "Message 6", "Message 7", "Message 8" }), "Backlog ordering survives retention pruning");
                Check(first.Messages.SequenceEqual(window.Messages) && list.Items.Count == 5, "Tab and virtualized backlog stay synchronized");

                await this.TestDisconnectedConnection();
                await this.TestIntentionalDisconnect();
                await this.TestWorkbenchConnection();
                window.Close();
                results.Add("PASS Desktop smoke test completed");
            } catch (Exception ex) {
                results.Add("FAIL " + ex);
                Environment.ExitCode = 1;
            } finally {
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "desktop-smoke-results.txt"), results);
                this.Exit();
            }
        }

        private async Task TestDisconnectedConnection() {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverKey = PublicKeyBox.GenerateKeyPair();
            this.Config.TrustedKeys.Add(new TrustedKey("Smoke server", serverKey.PublicKey));
            var accept = listener.AcceptTcpClientAsync();
            this.Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
            using (var peer = await accept.WaitAsync(TimeSpan.FromSeconds(3))) {
                var stream = peer.GetStream();
                await stream.ReadExactlyAsync(new byte[3]);
                var handshake = await KeyExchange.ServerHandshake(serverKey, stream);
                await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
                await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, Message(99));
                // EOF in a partially received frame must release the desktop connection too.
                await stream.WriteAsync(new byte[] { 32, 0 });
            }
            for (int i = 0; i < 40 && this.Connected; i++) await Task.Delay(50);
            this.Check(!this.Connected, "Desktop clears connection state after truncated-frame EOF");
            this.Check(this.Window.Messages.Any(message => message.ContentText.StartsWith("Message 99:")), "Last complete message is delivered before EOF cleanup");
            await this.Notifier.FlushAsync();
            this.Check(notificationSink.Deliveries.Count(d => d.Candidate.Kind == NotificationKind.ConnectionLost) == 1,
                "Truncated EOF produces exactly one abnormal disconnect notification");
        }

        private void Check(bool condition, string message) {
            if (!condition) throw new Exception(message);
            results.Add("PASS " + message);
        }

        private async Task TestWorkbenchConnection() {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "xivchat-desktop-history-" + Guid.NewGuid().ToString("N"));
            var store = await XIVChatStorage.HistoryStore.OpenAsync(Path.Combine(tempDirectory, "history.db"));
            this.Session.Store = store;
            try {
                this.Config.LocalBacklogMessages = 100;
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var key = PublicKeyBox.GenerateKeyPair();
                this.Config.TrustedKeys.Add(new TrustedKey("Workbench smoke", key.PublicKey));
                var accept = listener.AcceptTcpClientAsync();
                this.Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
                using (var peer = await accept.WaitAsync(TimeSpan.FromSeconds(3))) {
                    var stream = peer.GetStream();
                    await stream.ReadExactlyAsync(new byte[3]);
                    var handshake = await KeyExchange.ServerHandshake(key, stream);
                    var preferences = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
                    this.Check(preferences[0] == (byte)XIVChatCommon.Message.Client.ClientOperation.Preferences, "New desktop begins with backward-compatible preferences");
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx,
                        new ServerCapabilities { ServiceId = "smoke", RunId = "run", CursorBacklog = true, FriendSnapshots = true,
                            ChannelSubscriptions = true, GuardedCommands = true, DirectedTell = true, FriendPresence = true, GameEvents = true });
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    var request = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(request[0] == (byte)ClientOperation.Preferences && ClientPreferences.Decode(request[1..]).Channels == null,
                        "History-enabled desktop negotiates all channels");
                    request = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(request[0] == (byte)XIVChatCommon.Message.Client.ClientOperation.History, "Capabilities enable cursor recovery");
                    var owner = new CharacterIdentity { ContentId = 1, Name = "Aina Snow", HomeWorldId = 1, HomeWorld = "Home" };
                    var live = Message(2); live.Owner = owner; live.ServiceId = "smoke"; live.RunId = "run"; live.Sequence = 2; live.MessageId = "smoke:run:2";
                    // The server's framework-thread PlayerData response may arrive after live/history packets.
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, live);
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx,
                        new PlayerData("Home", "Visiting", "Area", "Aina Snow") { Identity = owner, OwnerEpoch = "login1" });
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new Availability(true));
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new ServerChannel(InputChannel.Say, "Say") { Revision = 42 });
                    var older = Message(1); older.Owner = owner; older.ServiceId = "smoke"; older.RunId = "run"; older.Sequence = 1; older.MessageId = "smoke:run:1";
                    var foreign = Message(3); foreign.Owner = new CharacterIdentity { ContentId = 2, Name = "Aina Snow", HomeWorldId = 2 };
                    foreign.ServiceId = "smoke"; foreign.RunId = "run"; foreign.Sequence = 3; foreign.MessageId = "smoke:run:3";
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new ServerHistory {
                        Messages = new[] { older, live, foreign }, Through = 3,
                        Cursor = new HistoryCursor { ServiceId = "smoke", RunId = "run", Sequence = 3 },
                    });
                    var source = Convert.ToHexString(key.PublicKey);
                    for (int i = 0; i < 100 && (await store.GetCursorAsync(source, "smoke"))?.Sequence != 3; i++) await Task.Delay(30);
                    await Task.Delay(200);
                    this.Check(this.Session.Messages.Where(m => m.MessageId != null).Select(m => m.Sequence).SequenceEqual(new long[] { 1, 2 }),
                        "Pending identity, replay deduplication and stream ordering share one view");
                    this.Check((await store.SearchAsync(new XIVChatStorage.HistoryQuery())).Count == 3, "Foreign-role history persists without entering the active view");
                    this.Check((await store.GetCursorAsync(source, "smoke"))?.Sequence == 3, "Recovery checkpoint follows committed records");
                    var friendRaw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(friendRaw[0] == (byte)XIVChatCommon.Message.Client.ClientOperation.PlayerList, "Desktop automatically requests friends after identity arrives");
                    var friendRequest = XIVChatCommon.Message.Client.ClientPlayerList.Decode(friendRaw[1..]);
                    this.Check(friendRequest.ExpectedOwnerKey == owner.Key && friendRequest.ExpectedOwnerEpoch == "login1", "Friend request carries login ownership");
                    var snapshot = new ServerPlayerList(PlayerListType.Friend, Enumerable.Range(1, 70).Select(i =>
                        new Player { ContentId = (ulong)i, Name = "Friend " + i, HomeWorld = 1 }).ToArray()) {
                        Owner = owner, OwnerEpoch = "login1", SnapshotId = "snapshot1", CapturedAt = DateTime.UtcNow,
                    };
                    snapshot.Players[69].Name = ""; snapshot.Players[69].HomeWorld = 0; snapshot.Players[69].IdentityUnavailable = true;
                    snapshot.Players[0].Name = "Peer Name"; snapshot.Players[0].HomeWorld = 7; snapshot.Players[0].HomeWorldName = "PeerWorld";
                    var pages = FriendListProtocol.Pages(snapshot, friendRequest.RequestId);
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, pages[0]);
                    await Task.Delay(150);
                    this.Check(this.Session.Friends.Snapshot == null && await store.GetFriendSnapshotAsync(source, owner.Key!) == null,
                        "Incomplete friend pages stay out of visible state and SQLite");
                    foreach (var page in pages.Skip(1)) await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, page);
                    for (int i = 0; i < 100 && await store.GetFriendSnapshotAsync(source, owner.Key!) == null; i++) await Task.Delay(30);
                    this.Check(this.Session.Friends.Snapshot?.Players.Length == 70 && !this.Session.Friends.IsStale &&
                        (await store.GetFriendSnapshotAsync(source, owner.Key!))?.Players[69].IdentityUnavailable == true,
                        "Complete friend snapshot including unavailable identity reaches shared state and SQLite");
                    this.Check(this.Connection!.RefreshFriendPresence(1), "Modern desktop can actively query one friend");
                    var presenceRaw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    var presenceRequest = ClientFriendPresence.Decode(presenceRaw[1..]);
                    this.Check(presenceRaw[0] == (byte)ClientOperation.FriendPresence && presenceRequest.ContentId == 1 && presenceRequest.OwnerKey == owner.Key && presenceRequest.OwnerEpoch == "login1", "Presence request preserves CID and login ownership");
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new ServerFriendPresence {
                        RequestId = presenceRequest.RequestId, OwnerKey = owner.Key!, OwnerEpoch = "login1", ContentId = 1,
                        Presence = PresenceState.Online, Status = FriendListStatus.Success, CheckedAt = DateTime.UtcNow,
                    });
                    for (int i = 0; i < 50 && this.Session.Friends.Presence.Get(1) == null; i++) await Task.Delay(30);
                    this.Check(this.Session.Friends.Presence.Get(1)?.Presence == PresenceState.Online && !this.Connection.RefreshFriendPresence(1), "Presence response becomes visible and repeated queries are limited");
                    this.Check(this.Connection!.RefreshFriends(), "Manual friend refresh is available");
                    friendRaw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    friendRequest = XIVChatCommon.Message.Client.ClientPlayerList.Decode(friendRaw[1..]);
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, FriendListProtocol.Error(friendRequest.RequestId, owner, "login1", FriendListStatus.Busy));
                    await Task.Delay(150);
                    this.Check(this.Session.Friends.Snapshot?.Players.Length == 70 && this.Session.Friends.IsStale, "Refresh error keeps the last complete snapshot");

                    this.Connection!.ChangeChannel(InputChannel.Party);
                    this.Check(this.Connection.SendMessage("smoke message"), "Ready desktop queues a guarded message");
                    var channelRaw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    var messageRaw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    var command = ClientMessage.Decode(messageRaw[1..]);
                    this.Check(channelRaw[0] == (byte)ClientOperation.Channel && messageRaw[0] == (byte)ClientOperation.Message &&
                        ClientChannel.Decode(channelRaw[1..]).ExpectedOwnerEpoch == "login1" && command.ExpectedOwnerKey == owner.Key &&
                        command.ExpectedOwnerEpoch == "login1" && command.ExpectedChannelRevision == 42,
                        "Channel and message preserve submission order and captured owner / target revision");
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new ServerCommandResult {
                        RequestId = command.RequestId, Failure = CommandFailure.ChannelChanged,
                    });
                    await Task.Delay(150);
                    this.Check(this.Session.Messages.Any(m => m.ContentText == LocalizationHelper.GetString("Command.ChannelChanged")),
                        "Rejected guarded command displays a localized cancellation reason");
                    await TestConversationUi(stream, handshake.Keys.rx, handshake.Keys.tx, owner, source, timeout.Token);
                    foreach (var tab in this.Config.Tabs) tab.Filter.Types = new HashSet<FilterType> { FilterType.Say };
                    this.Config.Notifications.Add(new Notification("Tell alert") { MatchAll = true, Channels = new List<ChatType> { ChatType.TellIncoming } });
                    this.Config.HistoryEnabled = false;
                    var subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels!.OrderBy(x => x).SequenceEqual(new ushort[] { (ushort)ChatType.Say, (ushort)ChatType.TellIncoming, (ushort)ChatType.TellOutgoing }.OrderBy(x => x)),
                        "Disabling local history retains the union of visible and notification channels");
                    foreach (var tab in this.Config.Tabs) tab.Filter.Types.Clear();
                    this.Connection.UpdateSubscriptions();
                    subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels!.Order().SequenceEqual(new ushort[] { (ushort)ChatType.TellOutgoing, (ushort)ChatType.TellIncoming }.Order()),
                        "Removing visible channels preserves notification subscriptions");
                    this.Config.HistoryEnabled = true;
                    subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels == null, "Re-enabling history restores all-channel delivery");
                    await TestNotificationsAsync(stream, handshake.Keys.tx, owner, source);
                    this.Window.Navigate("channels");
                    var channelComposer = Find<TextBox>((DependencyObject)this.Window.Content, t => t.Name == "Composer");
                    channelComposer.Text = "owner one channel draft";
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new PlayerData("Home", "Home", "Area", "Other") {
                        Identity = foreign.Owner, OwnerEpoch = "login2",
                    });
                    await Task.Delay(150);
                    foreach (var page in pages) await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, page);
                    await Task.Delay(150);
                    this.Check(this.Session.Friends.OwnerKey == "cid:2" && this.Session.Friends.Snapshot == null,
                        "Late friend pages cannot enter the next role");
                    this.Check(this.Session.Friends.Presence.Get(1) == null, "Role changes clear previously online friends");
                    this.Check(channelComposer.Text.Length == 0, "Changing characters isolates channel drafts");
                    await TestOldNotificationAsync();
                }
                for (int i = 0; i < 100 && this.Connected; i++) await Task.Delay(30);
                this.Check(!this.Connected, "New-protocol connection closes after EOF");
                this.Session.SetPlayer(new PlayerData("Other", "Other", "Area", "Aina Snow") {
                    Identity = new CharacterIdentity { ContentId = 2, Name = "Aina Snow", HomeWorldId = 2 },
                });
                this.Check(this.Session.Messages.Count == 0, "Changing own character clears the active view");
            } finally {
                this.Disconnect();
                await this.Notifier.FlushAsync();
                await this.Workbench.FlushAsync();
                this.Session.Store = null;
                await store.DisposeAsync();
                // Navigation badge readers can still hold a SQLite handle while their queued result completes.
                for (var attempt = 0; ; attempt++) {
                    try { Directory.Delete(tempDirectory, true); break; }
                    catch (IOException) when (attempt < 30) { await Task.Delay(50); }
                }
            }
        }

        private async Task TestConversationUi(NetworkStream stream, byte[] rx, byte[] tx, CharacterIdentity owner, string source, CancellationToken token) {
            var root = (DependencyObject)this.Window.Content;
            var target = new CharacterIdentity { ContentId = 1, Name = "Peer Name", HomeWorldId = 7, HomeWorld = "PeerWorld" };
            this.Window.Navigate("friends");
            var friends = Find<ListView>(root, v => v.Name == "FriendList");
            this.Check(friends.Items.Count == 70 && friends.Items.OfType<FriendRow>().Count(f => f.Peer == null) == 1, "Friend sidebar retains all snapshot rows with unavailable identities marked");
            this.Check(friends.Items.OfType<FriendRow>().Single(f => f.Name == "Peer Name").Status.Contains(LocalizationHelper.GetString("FriendList.Online")), "Friend sidebar displays queried online status");
            friends.SelectedItem = friends.Items.OfType<FriendRow>().Single(f => f.Peer == null);
            this.Check(this.Workbench.Conversations.Count == 0, "Unavailable friend cannot open a guessed Tell target");
            friends.SelectedItem = friends.Items.OfType<FriendRow>().Single(f => f.Name == "Peer Name");
            var model = this.Workbench.Conversations.Single();
            this.Check(Find<TextBlock>(root, t => t.Name == "ChatPresenceText").Text.Contains(LocalizationHelper.GetString("FriendList.Online")), "Conversation header displays queried presence even when list refresh fails");
            this.Check(TellTarget.From(model.Peer) == TellTarget.From(target), "Complete friend opens its exact home-world conversation");
            this.Window.Navigate("conversations"); this.Window.ShowConversation(model);
            await Task.Delay(100);
            var composer = Find<TextBox>(root, t => t.Name == "Composer");
            composer.Text = "fixed target draft"; model.Pinned = true; model.Note = "private note";
            this.Workbench.Save(model); await this.Workbench.FlushAsync();
            var store = this.Session.Store!;
            var saved = (await store.GetConversationsAsync(source, owner.Key!)).Single();
            this.Check(saved.State is { Draft: "fixed target draft", Pinned: true, Note: "private note" }, "Conversation draft, pin and note persist in the owner partition");
            this.Window.Navigate("channels"); this.Window.Navigate("conversations"); this.Window.ShowConversation(model);
            this.Check(composer.Text == "fixed target draft", "Navigation restores the conversation draft");
            this.Check(this.Workbench.Send(model, composer.Text), "Fixed-target Tell queues from the shared conversation");
            var raw = await SecretMessage.ReadSecretMessage(stream, rx, token);
            var sent = ClientMessage.Decode(raw[1..]);
            this.Check(raw[0] == (byte)ClientOperation.Message && sent.TellTarget == TellTarget.From(target) && sent.ExpectedOwnerKey == owner.Key && sent.ExpectedOwnerEpoch == "login1" && composer.Text.Length == 0,
                "Tell wire captures explicit peer and owner while clearing the submitted draft");
            await SecretMessage.SendSecretMessage(stream, tx, new ServerCommandResult { RequestId = sent.RequestId, Stage = CommandStage.Queued });
            await Task.Delay(80);
            this.Check(model.SendStatus == LocalizationHelper.GetString("Conversation.Queued"), "Queued status does not claim delivery");
            await SecretMessage.SendSecretMessage(stream, tx, new ServerCommandResult { RequestId = sent.RequestId, Stage = CommandStage.Submitted });
            await Task.Delay(80);
            this.Check(model.SendStatus == LocalizationHelper.GetString("Conversation.Submitted"), "Game submission status is shown separately");
            var connectedSession = this.Connection;
            this.Connection = null;
            this.Check(Find<TextBlock>(root, b => b.Name == "ComposerStatus").Text == LocalizationHelper.GetString("Conversation.ReadOnly")
                && !Find<Button>(root, b => b.Name == "SendButton").IsEnabled, "Disconnected composer shows read-only guidance instead of the previous send result");
            this.Connection = connectedSession;
            this.Check(this.Workbench.Send(model, "recover me"), "Second Tell queues");
            raw = await SecretMessage.ReadSecretMessage(stream, rx, token); sent = ClientMessage.Decode(raw[1..]);
            composer.Text = "new draft";
            await SecretMessage.SendSecretMessage(stream, tx, new ServerCommandResult { RequestId = sent.RequestId, Failure = CommandFailure.IdentityChanged });
            await Task.Delay(80);
            this.Check(model.Draft == "new draft" && model.FailedDraft == "recover me", "Rejection retains both the new draft and failed text");
            var restore = Find<Button>(root, b => b.Name == "RestoreDraftButton"); Invoke(restore);
            await Task.Delay(100);
            this.Check(composer.Text.Replace("\r\n", "\n").Replace('\r', '\n') == "new draft\nrecover me", "Failed text restores without overwriting newer typing: " + Newtonsoft.Json.JsonConvert.SerializeObject(composer.Text));
            this.Window.Navigate("channels");
            var incoming = Message(10); incoming.Channel = ChatType.TellIncoming; incoming.Owner = owner; incoming.TellPeer = target;
            incoming.ServiceId = "smoke"; incoming.RunId = "run"; incoming.Sequence = 10; incoming.MessageId = "smoke:run:10";
            await SecretMessage.SendSecretMessage(stream, tx, incoming);
            for (int i = 0; i < 50 && model.Unread == 0; i++) await Task.Delay(20);
            this.Check(model.Unread == 1, "Inactive conversation counts a live incoming Tell");
            await this.Workbench.MarkReadAsync(model);
            this.Check((await store.GetConversationsAsync(source, owner.Key!)).Single().Unread == 0, "Shared read state commits to SQLite");
            var search = Find<AutoSuggestBox>(root, b => b.Name == "GlobalSearch"); search.Text = "示例";
            this.Window.Navigate("history");
            await Task.Delay(300);
            var history = Find<ListView>(root, v => v.Name == "HistoryList");
            this.Check(history.Items.Count >= 4 && history.Items.OfType<HistoryResult>().All(r => r.Query == "示例"), "History page queries SQLite and supplies the literal highlight term");
            history.SelectedItem = history.Items.OfType<HistoryResult>().Single(r => r.Row.Message.MessageId == incoming.MessageId);
            Invoke(Find<Button>(root, b => b.Name == "HistoryBookmarkButton")); await Task.Delay(100);
            this.Check((await store.GetMessageAsync(XIVChatStorage.HistoryStore.StorageId(source, incoming)))?.Bookmarked == true, "History bookmark action persists");
            Invoke(Find<Button>(root, b => b.Name == "HistoryContextButton")); await Task.Delay(150);
            this.Check(Find<Button>(root, b => b.Name == "BackHistoryButton").Visibility == Visibility.Visible && this.Window.GetCurrentInputBox() == null,
                "Search context is read-only and offers return navigation");
            this.Window.Navigate("channels");
        }
        private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

        private static ServerMessage Message(int index) {
            var text = $"Message {index}: 示例聊天消息 " + (index % 7 == 0 ? new string('W', 150) : "short");
            return new ServerMessage(DateTime.UtcNow, ChatType.Say, Array.Empty<byte>(), Encoding.UTF8.GetBytes(text), new List<Chunk> { new TextChunk(text) });
        }

        private static ServerMessage FirstVisible(ListView list) {
            var panel = (ItemsStackPanel)list.ItemsPanelRoot;
            return ((ChatMessageRow)list.Items[panel.FirstVisibleIndex]).Message;
        }

        private static T Find<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject {
            return Descendants<T>(root).First(item => predicate == null || predicate(item));
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T item) yield return item;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
    }
}
