using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sodium;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    internal sealed partial class DesktopSmokeApp {
        private readonly RecordingNotifications notificationSink = new();
        private NotificationTarget? oldNotificationTarget;
        private sealed class RecordingNotifications : INotificationSink {
            public List<NotificationDelivery> Deliveries { get; } = new();
            public bool Show(NotificationDelivery delivery) { Deliveries.Add(delivery); return true; }
            public void Dispose() { }
        }

        private async Task TestIntentionalDisconnect() {
            var before = notificationSink.Deliveries.Count;
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var key = PublicKeyBox.GenerateKeyPair(); this.Config.TrustedKeys.Add(new TrustedKey("Manual disconnect smoke", key.PublicKey));
            var accept = listener.AcceptTcpClientAsync();
            this.Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
            using (var peer = await accept.WaitAsync(TimeSpan.FromSeconds(3))) {
                var stream = peer.GetStream(); await stream.ReadExactlyAsync(new byte[3]);
                var handshake = await KeyExchange.ServerHandshake(key, stream);
                await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
                this.Disconnect(); await Task.Delay(300); await this.Notifier.FlushAsync();
                Check(notificationSink.Deliveries.Count == before, "Intentional disconnect produces no notification");
            }
            var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            this.Connect("127.0.0.1", port);
            for (int i = 0; i < 50 && this.Connected; i++) await Task.Delay(50);
            await this.Notifier.FlushAsync();
            Check(!this.Connected && notificationSink.Deliveries.Count == before, "Initial connection refusal produces no abnormal disconnect notification");
        }

        private async Task TestNotificationsAsync(NetworkStream stream, byte[] tx, CharacterIdentity owner, string source) {
            var store = this.Session.Store!; var root = (DependencyObject)this.Window.Content;
            var peer = this.Workbench.Conversations.Single().Peer;
            var before = notificationSink.Deliveries.Count;
            this.Window.Navigate("history");
            ServerMessage Tell(int seq) {
                var message = Message(seq); message.Channel = ChatType.TellIncoming; message.Owner = owner; message.TellPeer = peer;
                message.ServiceId = "smoke"; message.RunId = "run"; message.Sequence = seq; message.MessageId = "smoke:run:" + seq;
                return message;
            }
            await SecretMessage.SendSecretMessage(stream, tx, Tell(21));
            await SecretMessage.SendSecretMessage(stream, tx, Tell(22));
            for (int i = 0; i < 70 && notificationSink.Deliveries.Count == before; i++) await Task.Delay(50);
            var batch = notificationSink.Deliveries.Skip(before).Single();
            Check(batch.Candidate.Kind == NotificationKind.Tell && batch.Count == 2, "Live encrypted Tells merge into one notification");
            oldNotificationTarget = batch.Candidate.Target;
            await this.Window.OpenNotificationTargetAsync(oldNotificationTarget); await Task.Delay(150);
            Check(this.Window.GetCurrentInputBox() != null && this.Window.IsReadingNotification(oldNotificationTarget),
                "Tell notification opens its owned conversation and detects foreground reading");
            before = notificationSink.Deliveries.Count;
            await SecretMessage.SendSecretMessage(stream, tx, Tell(23)); await Task.Delay(2600);
            Check(notificationSink.Deliveries.Count == before, "Visible latest Tell conversation suppresses notifications");
            this.Config.NotificationOptions.DoNotDisturb = true;
            this.Window.Navigate("history");
            ServerGameEvent Event(string id, int age = 0) => new() {
                EventId = id, ServiceId = "smoke", RunId = "run", Owner = owner, OwnerEpoch = "login1", Kind = GameEventKind.DutyReady,
                Timestamp = DateTime.UtcNow.AddSeconds(-age), ExpiresAt = DateTime.UtcNow.AddSeconds(45-age), Name = "Smoke duty", DataId = 42,
            };
            await SecretMessage.SendSecretMessage(stream, tx, Event("dnd")); await Task.Delay(150);
            Check((await store.GetEventsAsync(new(source, owner.Key))).Count == 1 && notificationSink.Deliveries.Count == before,
                "DND records a duty event without showing it");
            this.Config.NotificationOptions.DoNotDisturb = false;
            await SecretMessage.SendSecretMessage(stream, tx, Event("expired", 60)); await Task.Delay(150);
            Check((await store.GetEventsAsync(new(source, owner.Key))).Count == 2 && notificationSink.Deliveries.Count == before,
                "Expired duties remain in history without alerts");
            var ready = Event("ready");
            await SecretMessage.SendSecretMessage(stream, tx, ready); await Task.Delay(150);
            Check(notificationSink.Deliveries.Count == before + 1 && notificationSink.Deliveries.Last().Candidate.Kind == NotificationKind.DutyReady,
                "Negotiated game event reaches notification delivery");
            await SecretMessage.SendSecretMessage(stream, tx, ready); await Task.Delay(100);
            Check(notificationSink.Deliveries.Count == before + 1 && (await store.GetEventsAsync(new(source, owner.Key))).Count == 3,
                "Duplicate game packets do not duplicate records or notifications");
            var foreign = Event("old-role"); foreign.OwnerEpoch = "old-login";
            await SecretMessage.SendSecretMessage(stream, tx, foreign); await Task.Delay(100);
            Check(notificationSink.Deliveries.Count == before + 1, "Old login event cannot alert in the new login");
            var malformed = Event("wrong-run"); malformed.RunId = "other";
            await SecretMessage.SendSecretMessage(stream, tx, malformed); await Task.Delay(100);
            Check(await store.GetEventAsync(HistoryStore.EventStorageId(source, malformed)) == null, "Foreign server run event rejected before storage");
            await this.Window.OpenNotificationTargetAsync(notificationSink.Deliveries.Last().Candidate.Target);
            Check(Find<ListView>(root, v => v.Name == "EventsList").SelectedItem is EventListItem { Row.Event.EventId: "ready" } &&
                (await store.GetEventAsync(HistoryStore.EventStorageId(source, ready)))?.Read == true,
                "Event activation locates the exact row and persists its read state");
            var aged = Event("retained", 100 * 86400);
            await this.Notifier.ReceiveEventAsync("retention", aged, null, false);
            await store.PruneAsync(90, DateTime.UtcNow);
            Check((await this.Notifier.GetEventsAsync(new("retention"))).Count == 0 &&
                await this.Notifier.GetEventAsync(HistoryStore.EventStorageId("retention", aged)) == null &&
                !(await this.Notifier.GetOwnersAsync()).Any(o => o.Source == "retention"), "Retention cannot resurrect events or owners from memory");
            this.Config.Notifications.Add(new Notification("keyword") { Channels = new() { ChatType.Say, ChatType.TellOutgoing }, Substrings = new() { "示例" } });
            before = notificationSink.Deliveries.Count;
            var keyword = Tell(30); keyword.Channel = (ChatType)((ushort)ChatType.Say | 128); keyword.TellPeer = null;
            await SecretMessage.SendSecretMessage(stream, tx, keyword);
            var outgoing = Tell(31); outgoing.Channel = ChatType.TellOutgoing;
            await SecretMessage.SendSecretMessage(stream, tx, outgoing);
            for (int i = 0; i < 70 && notificationSink.Deliveries.Count == before; i++) await Task.Delay(50);
            var keywordDelivery = notificationSink.Deliveries.Skip(before).Single();
            Check(keywordDelivery.Candidate.Kind == NotificationKind.Keyword && keywordDelivery.Candidate.Target.Channel == (ushort)ChatType.Say,
                "Keyword notifications normalize channel flags and exclude outgoing Tells");
            this.Config.Notifications.Clear();
            var settings = new ConfigWindow(this.Config); settings.Activate(); await Task.Delay(100);
            var settingsTabs = (TabView)settings.Content;
            settingsTabs.SelectedItem = settingsTabs.TabItems.OfType<TabViewItem>().Single(tab => tab.Name == "TabNotifications");
            await Task.Delay(100);
            LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified); await Task.Delay(100);
            Check(Find<Button>((DependencyObject)settings.Content, b => Localize.GetContent(b) == "Notify.Test").Content?.ToString() == LocalizationHelper.GetString("Notify.Test"),
                "Notification settings switch language in an open window");
            LocalizationHelper.ApplyLanguage(AppLanguage.English); settings.Close(); this.Window.Activate();
        }

        private async Task TestOldNotificationAsync() {
            var root = (DependencyObject)this.Window.Content; var connection = this.Connection;
            await this.Window.OpenNotificationTargetAsync(oldNotificationTarget!);
            Check(this.Window.GetCurrentInputBox() == null && this.Workbench.OwnerKey == "cid:2" && ReferenceEquals(this.Connection, connection),
                "Old character notification opens read-only history without changing live owner or connection");
            this.Window.Navigate("events"); await Task.Delay(100);
            await this.Window.OpenNotificationTargetAsync(new(NotificationTargetKind.Message, oldNotificationTarget!.Source, "cid:1", Channel: (ushort)ChatType.Say));
            Check(Find<TextBlock>(root, b => b.Name == "FooterStatus").Text == LocalizationHelper.GetString("Notify.TargetMissing"),
                "Missing old-owner record cannot fall back to the current owner's channel");
        }
    }
}
