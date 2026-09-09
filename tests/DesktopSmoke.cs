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

    internal sealed class DesktopSmokeApp : App {
        private readonly List<string> results = new();
        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            // Do not run App.OnLaunched: the smoke test must not read or save the user's configuration.
            try {
                var first = new Tab("General") { Filter = Tab.GeneralFilter() };
                var second = new Tab("Also general") { Filter = Tab.GeneralFilter() };
                var config = new Configuration { LocalBacklogMessages = 10_000, BacklogMessages = 0, Tabs = new ObservableCollection<Tab> { first, second } };
                typeof(App).GetProperty(nameof(Config))!.SetValue(this, config);
                LocalizationHelper.Initialize(AppLanguage.English);
                var window = new MainWindow();
                typeof(App).GetProperty(nameof(Window))!.SetValue(this, window);
                window.Activate();
                await Task.Delay(300);

                for (int i = 0; i < 10_000; i++) window.AddMessage(Message(i));
                await Task.Delay(1500);
                var tabs = Find<TabView>((DependencyObject)window.Content);
                var firstView = Find<ChatMessageList>((DependencyObject)((TabViewItem)tabs.SelectedItem).Content);
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
                            ChannelSubscriptions = true, GuardedCommands = true });
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
                    foreach (var tab in this.Config.Tabs) tab.Filter.Types = new HashSet<FilterType> { FilterType.Say };
                    this.Config.Notifications.Add(new Notification("Tell alert") { MatchAll = true, Channels = new List<ChatType> { ChatType.TellIncoming } });
                    this.Config.HistoryEnabled = false;
                    var subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels!.OrderBy(x => x).SequenceEqual(new ushort[] { (ushort)ChatType.Say, (ushort)ChatType.TellIncoming }.OrderBy(x => x)),
                        "Disabling local history retains the union of visible and notification channels");
                    foreach (var tab in this.Config.Tabs) tab.Filter.Types.Clear();
                    this.Connection.UpdateSubscriptions();
                    subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels!.SequenceEqual(new ushort[] { (ushort)ChatType.TellIncoming }),
                        "Removing visible channels preserves notification subscriptions");
                    this.Config.HistoryEnabled = true;
                    subscribed = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                    this.Check(ClientPreferences.Decode(subscribed[1..]).Channels == null, "Re-enabling history restores all-channel delivery");
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new PlayerData("Home", "Home", "Area", "Other") {
                        Identity = foreign.Owner, OwnerEpoch = "login2",
                    });
                    await Task.Delay(150);
                    foreach (var page in pages) await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, page);
                    await Task.Delay(150);
                    this.Check(this.Session.Friends.OwnerKey == "cid:2" && this.Session.Friends.Snapshot == null,
                        "Late friend pages cannot enter the next role");
                }
                for (int i = 0; i < 100 && this.Connected; i++) await Task.Delay(30);
                this.Check(!this.Connected, "New-protocol connection closes after EOF");
                this.Session.SetPlayer(new PlayerData("Other", "Other", "Area", "Aina Snow") {
                    Identity = new CharacterIdentity { ContentId = 2, Name = "Aina Snow", HomeWorldId = 2 },
                });
                this.Check(this.Session.Messages.Count == 0, "Changing own character clears the active view");
            } finally {
                this.Disconnect();
                this.Session.Store = null;
                await store.DisposeAsync();
                Directory.Delete(tempDirectory, true);
            }
        }

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
