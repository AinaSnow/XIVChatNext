using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    /// <summary>UI-thread shared message state; persistence belongs to the application, not a window.</summary>
    public sealed class ChatSession {
        private readonly Func<Configuration> configuration;
        private readonly HashSet<string> seen = new();
        private readonly Queue<string> seenOrder = new();
        private readonly Queue<ServerMessage> pendingIdentity = new();
        private int lastSequence = -1;
        private int insertAt;
        private long lastCleanupUtcTicks;
        private Exception? storageError;
        public List<ServerMessage> Messages { get; } = new();
        public FriendListSession Friends { get; } = new();
        public PlayerData? Player { get; private set; }
        public HistoryStore? Store { get; set; }
        public Exception? StorageError => Volatile.Read(ref this.storageError);
        public event Action<Exception>? PersistenceFailed;
        private string source = "local";
        public string Source { get => this.source; set { if (this.source == value) return; this.source = value; this.ContextChanged?.Invoke(); } }
        public event Action? ContextChanged;
        public event Action<ServerMessage[], bool>? MessagesChanged;
        public event Action? Cleared;

        public ChatSession(Func<Configuration> configuration) => this.configuration = configuration;

        public async Task RecordAsync(ServerMessage message, string source, bool live = false) {
            var store = this.Store;
            if (message.Channel == 0 || !this.configuration().HistoryEnabled || store == null || this.StorageError != null) return;
            try {
                await store.AppendAsync(source, message, HistoryStore.StorageId(source, message), live);
                var now = DateTime.UtcNow;
                var last = Interlocked.Read(ref this.lastCleanupUtcTicks);
                if (now.Ticks - last > TimeSpan.TicksPerDay && Interlocked.CompareExchange(ref this.lastCleanupUtcTicks, now.Ticks, last) == last) {
                    await store.PruneAsync(this.configuration().HistoryRetentionDays, now);
                }
            } catch (Exception ex) {
                this.ReportStorageError(ex);
            }
        }

        public void ReportStorageError(Exception ex) {
            if (Interlocked.CompareExchange(ref this.storageError, ex, null) == null) this.PersistenceFailed?.Invoke(ex);
        }

        public void SetPlayer(PlayerData? player) {
            var pending = this.pendingIdentity.ToArray();
            this.pendingIdentity.Clear();
            if (player != null && this.Player?.Identity?.Key != player.Identity?.Key) this.Clear();
            this.Player = player;
            this.ContextChanged?.Invoke();
            if (player?.Identity?.Key != null) this.AddCursorPage(pending);
        }

        public bool Add(ServerMessage message) {
            if (!this.Accept(message)) return false;
            this.Messages.Add(message);
            foreach (var tab in this.configuration().Tabs) tab.AddMessage(message, this.configuration());
            this.Prune();
            this.MessagesChanged?.Invoke(new[] { message }, true);
            return true;
        }

        public void AddBacklog(ServerMessage[] messages, int sequence) {
            var accepted = messages.Where(this.Accept).ToArray();
            if (accepted.Length == 0) return;
            if (sequence != this.lastSequence) {
                this.lastSequence = sequence;
                this.insertAt = this.Messages.Count;
            }
            this.Messages.InsertRange(this.insertAt, accepted);
            foreach (var tab in this.configuration().Tabs) tab.AddReversedChunk(accepted, sequence, this.configuration());
            this.Prune();
            this.MessagesChanged?.Invoke(accepted, false);
        }

        public void AddCursorPage(ServerMessage[] messages) {
            // Cursor pages arrive oldest first and can interleave live traffic. Merge by stream order.
            var accepted = messages.Where(this.Accept).ToArray();
            if (accepted.Length == 0) return;
            foreach (var message in accepted) {
                var index = this.Messages.FindIndex(existing => Compare(message, existing) < 0);
                if (index < 0) index = this.Messages.Count;
                this.Messages.Insert(index, message);
            }
            foreach (var tab in this.configuration().Tabs) tab.MergeHistory(accepted, this.configuration());
            this.Prune();
            this.insertAt = this.Messages.Count;
            this.MessagesChanged?.Invoke(accepted, false);
        }

        internal static int Compare(ServerMessage left, ServerMessage right) {
            if (left.ServiceId != null && left.ServiceId == right.ServiceId && left.RunId == right.RunId)
                return left.Sequence.CompareTo(right.Sequence);
            return left.Timestamp.CompareTo(right.Timestamp);
        }

        private bool Accept(ServerMessage message) {
            if (message.Channel == 0) return true;
            if (message.Owner?.Key != null && this.Player?.Identity?.Key == null) {
                this.pendingIdentity.Enqueue(message);
                while (this.pendingIdentity.Count > Math.Max(1L, this.configuration().LocalBacklogMessages)) this.pendingIdentity.Dequeue();
                return false;
            }
            // A new server explicitly owns each record. Never merge another character into the active view.
            if (this.Player?.Identity?.Key != null && message.Owner?.Key == null) return false;
            if (message.Owner?.Key is { } owner && owner != this.Player?.Identity?.Key) return false;
            if (string.IsNullOrEmpty(message.MessageId)) return true;
            var id = this.Source + "/" + message.MessageId;
            if (!this.seen.Add(id)) return false;
            this.seenOrder.Enqueue(id);
            while (this.seenOrder.Count > Math.Max(20_000L, this.configuration().LocalBacklogMessages * 2L)) this.seen.Remove(this.seenOrder.Dequeue());
            return true;
        }

        private void Prune() {
            var diff = this.Messages.Count - this.configuration().LocalBacklogMessages;
            if (diff <= 0) return;
            this.Messages.RemoveRange(0, (int)diff);
            this.insertAt = Math.Max(0, this.insertAt - (int)diff);
        }

        public void Clear() {
            this.Messages.Clear();
            foreach (var tab in this.configuration().Tabs) tab.ClearMessages();
            this.lastSequence = -1;
            this.insertAt = 0;
            this.seen.Clear();
            this.seenOrder.Clear();
            this.pendingIdentity.Clear();
            this.Cleared?.Invoke();
        }
    }
}
