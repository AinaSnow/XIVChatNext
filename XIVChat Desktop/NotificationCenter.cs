using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public interface INotificationSink : IDisposable {
        bool Show(NotificationDelivery delivery);
        Task ClearAsync() => Task.CompletedTask;
    }

    public sealed class NotificationCenter : IDisposable {
        private sealed record Remembered(EventRow Row, bool Persisted);
        private readonly App app;
        private readonly NotificationPolicy policy = new();
        private readonly Dictionary<string, Remembered> events = new();
        private readonly Queue<string> eventOrder = new();
        private readonly ConcurrentDictionary<Task, byte> writes = new();
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly string localRun = Guid.NewGuid().ToString("N");
        private NotificationTarget? pendingTarget;
        private bool disposed;
        public INotificationSink? Sink { get; set; }
        public string? PlatformError { get; private set; }
        public event Action? EventsChanged;
        public event Action? StatusChanged;

        public NotificationCenter(App app) {
            this.app = app;
            timer.Tick += Tick; timer.Start();
            app.Presentation.PolicyChanged += PrivacyChanged;
        }

        public void InitializePlatform() {
            try { Sink = new WindowsNotificationSink(Activate); if (app.Presentation.Enabled) PrivacyChanged(); }
            catch (Exception ex) { PlatformError = ex.Message; StatusChanged?.Invoke(); }
        }

        public void Activate(NotificationTarget target) => app.Dispatch(() => {
            if (disposed || !target.Valid) return;
            if (app.Window == null) { pendingTarget = target; return; }
            _ = app.Workspace.OpenNotificationAsync(target);
        });

        public void WindowReady() {
            if (pendingTarget is not { } target) return;
            pendingTarget = null; Activate(target);
        }

        public void MessageReceived(ServerMessage message, Connection connection) {
            if (disposed || !ReferenceEquals(app.Connection, connection)) return;
            var channel = (ChatType)((ushort)message.Channel & 127);
            if (channel == ChatType.TellOutgoing) return;
            var options = app.Config.NotificationOptions;
            var tell = channel == ChatType.TellIncoming && options.Tell;
            if (!tell && (!options.Keyword || !app.Config.Notifications.Take(64).Any(rule => rule.Matches(message)))) return;
            var source = app.Session.Source;
            var owner = message.Owner?.Key ?? app.Session.Player?.Identity?.Key ?? "unassigned:" + source;
            var peer = ConversationIdentity.PeerKey(message.TellPeer) != null ? message.TellPeer : null;
            var target = new NotificationTarget(peer == null ? NotificationTargetKind.Message : NotificationTargetKind.Conversation,
                source, owner, message.MessageId == null ? null : HistoryStore.StorageId(source, message), peer, (ushort)channel);
            var context = app.Presentation.Observe(message);
            var title = peer != null ? app.Presentation.Identity(peer, context).Name : app.Presentation.Sender(message);
            if (string.IsNullOrEmpty(title)) title = L("Notify.KeywordTitle");
            var candidate = new NotificationCandidate(message.MessageId ?? Guid.NewGuid().ToString("N"), tell ? NotificationKind.Tell : NotificationKind.Keyword,
                target, title, Limit(app.Presentation.Content(message), 500), message.Timestamp, ConnectionId: connection.Id, OwnerEpoch: app.Session.Player?.OwnerEpoch);
            policy.Enqueue(candidate, options, DateTime.UtcNow, DateTime.Now, app.Workspace.IsReading(target));
        }

        public async Task ReceiveEventAsync(string source, ServerGameEvent entry, Connection? connection, bool notify = true) {
            if (!entry.IsValid()) return;
            var fresh = true;
            var persisted = false;
            if (app.Config.HistoryEnabled && app.Session.Store is { } store && app.Session.StorageError == null) {
                try { fresh = await store.AppendEventAsync(source, entry); persisted = true; }
                catch (Exception ex) { app.Session.ReportStorageError(ex); }
            }
            var id = HistoryStore.EventStorageId(source, entry);
            var owner = entry.Owner?.Key ?? "unassigned:" + source;
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            app.Dispatch(() => {
                try {
                    if (disposed || events.ContainsKey(id)) return;
                    events[id] = new Remembered(new EventRow(id, source, owner, entry, false), persisted);
                    eventOrder.Enqueue(id);
                    while (eventOrder.Count > 500) events.Remove(eventOrder.Dequeue());
                    EventsChanged?.Invoke();
                    if (!fresh || !notify || connection != null && !ReferenceEquals(app.Connection, connection)) return;
                    var kind = entry.Kind switch {
                        GameEventKind.DutyReady => NotificationKind.DutyReady,
                        GameEventKind.Login or GameEventKind.Logout => NotificationKind.LoginLogout,
                        GameEventKind.TerritoryChanged => NotificationKind.TerritoryChanged,
                        GameEventKind.ConnectionLost => NotificationKind.ConnectionLost,
                        _ => NotificationKind.Test,
                    };
                    var target = new NotificationTarget(NotificationTargetKind.Event, source, owner, id);
                    var candidate = new NotificationCandidate(entry.EventId, kind, target, L("Event." + entry.Kind),
                        app.Presentation.EventDetails(source, entry), entry.Timestamp, entry.ExpiresAt, connection?.Id, entry.OwnerEpoch);
                    policy.Enqueue(candidate, app.Config.NotificationOptions, DateTime.UtcNow, DateTime.Now);
                    Tick(null, null!);
                } catch (Exception ex) { applied.TrySetException(ex); }
                finally { applied.TrySetResult(); }
            });
            await applied.Task;
        }

        public void ConnectionLost(Connection connection) {
            var owner = connection.LastPlayer?.Identity;
            var entry = new ServerGameEvent {
                EventId = "connection/" + connection.Id, ServiceId = "desktop", RunId = localRun,
                Owner = owner == null ? null : ConversationIdentity.Copy(owner), OwnerEpoch = connection.LastPlayer?.OwnerEpoch ?? localRun,
                Kind = GameEventKind.ConnectionLost, Timestamp = DateTime.UtcNow, Name = connection.Endpoint,
            };
            Track(ReceiveEventAsync(connection.Source, entry, null));
        }

        public Task TestAsync() {
            var owner = app.Workbench.Owner;
            var entry = new ServerGameEvent {
                EventId = "test/" + Guid.NewGuid().ToString("N"), ServiceId = "desktop", RunId = localRun,
                Owner = owner == null ? null : ConversationIdentity.Copy(owner), OwnerEpoch = localRun,
                Kind = GameEventKind.NotificationTest, Timestamp = DateTime.UtcNow,
            };
            var task = ReceiveEventAsync(app.Workbench.Source.Length > 0 ? app.Workbench.Source : "local", entry, null);
            Track(task); return task;
        }
        internal string PreviewTest(NotificationOptions options) {
            if (options.IsQuiet(DateTime.Now)) return SetupText.T("当前免打扰设置阻止了测试通知。", "Your current quiet settings suppress the test notification.");
            if (Sink == null || PlatformError != null) return SetupText.T("Windows 通知暂不可用：", "Windows notifications are unavailable: ") + PlatformError;
            var candidate = new NotificationCandidate("setup/" + Guid.NewGuid().ToString("N"), NotificationKind.Test,
                new NotificationTarget(NotificationTargetKind.Event, "local", "setup"), L("Event.NotificationTest"), SetupText.T("点击返回初始设置。", "Click to return to initial setup."), DateTime.UtcNow);
            try { return Sink.Show(new NotificationDelivery(candidate, 1, options.Sound, "setup-test"))
                ? SetupText.T("测试通知已提交给 Windows。请确认横幅、声音与点击返回；若未出现，请检查系统通知和免打扰设置。", "The test was submitted to Windows. Check the banner, sound and click action. If absent, check system notifications and Do not disturb.")
                : SetupText.T("Windows 未接受测试通知，请检查系统通知设置。", "Windows did not accept the test notification. Check system notification settings."); }
            catch (Exception ex) { return ex.Message; }
        }

        private async void Track(Task task) {
            writes.TryAdd(task, 0);
            try { await task; }
            catch (Exception ex) { app.Session.ReportStorageError(ex); }
            finally { writes.TryRemove(task, out _); }
        }
        public Task FlushAsync() => Task.WhenAll(writes.Keys);

        private async void PrivacyChanged() {
            policy.ClearPending();
            try { if (Sink != null) await Sink.ClearAsync(); }
            catch (Exception ex) { PlatformError = ex.Message; StatusChanged?.Invoke(); }
        }

        private void Tick(object? sender, object args) {
            if (disposed) return;
            foreach (var delivery in policy.Drain(app.Config.NotificationOptions, DateTime.UtcNow, DateTime.Now,
                target => app.Workspace.IsReading(target), Applicable)) {
                try {
                    if (Sink?.Show(delivery) != true && PlatformError == null) {
                        PlatformError = L("Notify.SystemUnavailable"); StatusChanged?.Invoke();
                    }
                } catch (Exception ex) { PlatformError = ex.Message; StatusChanged?.Invoke(); }
            }
        }

        private bool Applicable(NotificationCandidate candidate) {
            if (candidate.ConnectionId == null) return true;
            var connection = app.Connection;
            if (connection?.Id != candidate.ConnectionId || candidate.Target.Source != app.Session.Source) return false;
            if (candidate.Kind == NotificationKind.LoginLogout) return true;
            var player = app.Session.Player;
            return candidate.Target.OwnerKey == (player?.Identity?.Key ?? "unassigned:" + app.Session.Source) &&
                candidate.OwnerEpoch == player?.OwnerEpoch;
        }

        public async Task<IReadOnlyList<EventRow>> GetEventsAsync(EventQuery query) {
            var rows = new Dictionary<string, EventRow>();
            var durable = false;
            if (app.Session.Store is { } store && app.Session.StorageError == null) {
                try { foreach (var row in await store.GetEventsAsync(query)) rows[row.Id] = row; durable = true; }
                catch (Exception ex) { app.Session.ReportStorageError(ex); }
            }
            foreach (var remembered in events.Values) {
                if (durable && remembered.Persisted) continue;
                var row = remembered.Row;
                if (!Matches(row, query)) continue;
                // Durable rows come only from SQLite, so retention cannot resurrect a cached row.
                if (!rows.ContainsKey(row.Id)) rows[row.Id] = row;
            }
            return rows.Values.OrderByDescending(r => Millis(r.Event.Timestamp)).ThenByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(Math.Clamp(query.Limit, 1, 200)).ToArray();
        }

        public async Task<EventRow?> GetEventAsync(string id) {
            events.TryGetValue(id, out var entry);
            if (entry?.Persisted == false) return entry.Row;
            if (app.Session.Store is not { } store || app.Session.StorageError != null) return entry?.Row;
            try { return await store.GetEventAsync(id); }
            catch (Exception ex) { app.Session.ReportStorageError(ex); return null; }
        }

        public async Task<IReadOnlyList<HistoryOwner>> GetOwnersAsync() {
            var owners = new Dictionary<string, HistoryOwner>();
            var durable = false;
            if (app.Session.Store is { } store && app.Session.StorageError == null) {
                try { foreach (var owner in await store.GetEventOwnersAsync()) owners[owner.Source + "/" + owner.OwnerKey] = owner; durable = true; }
                catch (Exception ex) { app.Session.ReportStorageError(ex); }
            }
            foreach (var item in events.Values.Where(e => !durable || !e.Persisted)) owners[item.Row.Source + "/" + item.Row.OwnerKey] = new HistoryOwner(item.Row.Source, item.Row.OwnerKey, item.Row.Event.Owner);
            return owners.Values.ToArray();
        }

        public async Task<int> UnreadCountAsync(string? source, string? owner) {
            var count = 0;
            var durable = app.Session.Store != null && app.Session.StorageError == null;
            if (durable) {
                try { count = await app.Session.Store!.GetUnreadEventCountAsync(source, owner); }
                catch (Exception ex) { app.Session.ReportStorageError(ex); durable = false; }
            }
            return count + events.Values.Count(e => !e.Row.Read && (!durable || !e.Persisted) && Matches(e.Row, new EventQuery(source, owner)));
        }

        public async Task MarkReadAsync(IEnumerable<EventRow> rows) {
            var ids = rows.Where(r => !r.Read).Select(r => r.Id).Distinct().Take(200).ToArray();
            if (ids.Length == 0) return;
            foreach (var id in ids) if (events.TryGetValue(id, out var entry)) events[id] = entry with { Row = entry.Row with { Read = true } };
            if (app.Session.Store is { } store && app.Session.StorageError == null) {
                try { await store.MarkEventsReadAsync(ids); }
                catch (Exception ex) { app.Session.ReportStorageError(ex); }
            }
            EventsChanged?.Invoke();
        }

        private static bool Matches(EventRow row, EventQuery query) =>
            (query.Source == null || query.Source == row.Source) && (query.OwnerKey == null || query.OwnerKey == row.OwnerKey) &&
            (query.Kind == null || query.Kind == row.Event.Kind) && (query.BeforeTimestampUtc == null || Millis(row.Event.Timestamp) < Millis(query.BeforeTimestampUtc.Value) ||
                Millis(row.Event.Timestamp) == Millis(query.BeforeTimestampUtc.Value) && string.CompareOrdinal(row.Id, query.BeforeId ?? "") < 0);
        private static long Millis(DateTime time) => new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeMilliseconds();

        public static string EventDetails(ServerGameEvent entry) => entry.Kind switch {
            GameEventKind.DutyReady => string.IsNullOrWhiteSpace(entry.Name) ? L("Event.UnknownDuty") : entry.Name,
            GameEventKind.TerritoryChanged => string.IsNullOrWhiteSpace(entry.Name) ? L("Event.UnknownTerritory") : entry.Name,
            GameEventKind.Login or GameEventKind.Logout => entry.Owner?.Name + " @ " + entry.Owner?.HomeWorld,
            GameEventKind.ConnectionLost => entry.Name,
            _ => L("Notify.TestBody"),
        };
        private static string Limit(string value, int count) => value.Length <= count ? value : value[..count] + "…";
        private static string L(string key) => LocalizationHelper.GetString(key);

        public void Dispose() {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Tick -= Tick; policy.ClearPending(); Sink?.Dispose();
            app.Presentation.PolicyChanged -= PrivacyChanged;
        }
    }
}
