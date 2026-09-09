using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    internal sealed record FriendRequest(Guid ClientId, ClientPlayerList Request, CancellationToken Cancellation);
    internal sealed record FriendReadResult(string OwnerEpoch, Player[] Players, FriendListStatus Status);

    internal interface IFriendListReader : IDisposable {
        void SetContext(CharacterIdentity? owner, string epoch);
        FriendListStatus? BeginRefresh();
        FriendReadResult? TakeResult();
        void Cancel();
    }

    // Only Enqueue is called from network threads. Everything else is driven by the framework update.
    internal sealed class FriendListCoordinator : IDisposable {
        private readonly IFriendListReader reader;
        private readonly Action<FriendRequest, ServerPlayerList> reply;
        private readonly Channel<FriendRequest> requests = Channel.CreateBounded<FriendRequest>(new BoundedChannelOptions(256) {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait,
        });
        private readonly Dictionary<Guid, FriendRequest> pending = new();
        private CharacterIdentity? owner;
        private string? epoch;
        private ServerPlayerList? cached;
        private bool refreshing;
        private DateTime deadline;
        private DateTime nextRefresh;
        public int LastCount => this.cached?.Players.Length ?? 0;
        public DateTime? LastUpdated => this.cached?.CapturedAt;
        public FriendListStatus LastStatus { get; private set; } = FriendListStatus.Unavailable;

        internal FriendListCoordinator(IFriendListReader reader, Action<FriendRequest, ServerPlayerList> reply) {
            this.reader = reader; this.reply = reply;
        }

        internal bool Enqueue(FriendRequest request) => this.requests.Writer.TryWrite(request);

        internal void Tick(CharacterIdentity? currentOwner, string currentEpoch, DateTime now) {
            if (this.epoch != currentEpoch || this.owner?.Key != currentOwner?.Key) {
                this.reader.Cancel();
                this.CompleteError(FriendListStatus.IdentityChanged);
                this.owner = currentOwner; this.epoch = currentEpoch; this.cached = null; this.refreshing = false;
                this.nextRefresh = DateTime.MinValue;
                this.reader.SetContext(currentOwner, currentEpoch);
                this.LastStatus = currentOwner?.Key == null ? FriendListStatus.NotLoggedIn : FriendListStatus.Unavailable;
            }
            foreach (var id in this.pending.Where(x => x.Value.Cancellation.IsCancellationRequested).Select(x => x.Key).ToArray()) this.pending.Remove(id);

            var completed = this.reader.TakeResult();
            if (this.refreshing && completed != null && completed.OwnerEpoch == this.epoch) {
                this.refreshing = false;
                if (completed.Status != FriendListStatus.Success || !FriendListProtocol.ValidPlayers(completed.Players)) {
                    this.CompleteError(completed.Status == FriendListStatus.Success ? FriendListStatus.Failed : completed.Status);
                } else {
                    this.cached = new ServerPlayerList(PlayerListType.Friend, completed.Players) {
                        Owner = this.owner, OwnerEpoch = this.epoch, SnapshotId = Guid.NewGuid().ToString("N"), CapturedAt = now,
                    };
                    this.LastStatus = FriendListStatus.Success;
                    foreach (var item in this.pending.Values) this.SendSnapshot(item);
                    this.pending.Clear();
                }
            }
            if (this.refreshing && now >= this.deadline) {
                this.reader.Cancel(); this.refreshing = false; this.CompleteError(FriendListStatus.TimedOut);
            }

            for (int i = 0; i < 64 && this.requests.Reader.TryRead(out var item); i++) {
                if (item.Cancellation.IsCancellationRequested) continue;
                var request = item.Request;
                if (request.Type != PlayerListType.Friend) continue;
                if (request.RequestId is { } requestId && (requestId.Length == 0 || requestId.Length > 64 ||
                    string.IsNullOrEmpty(request.ExpectedOwnerKey) || string.IsNullOrEmpty(request.ExpectedOwnerEpoch))) {
                    this.SendError(item, FriendListStatus.Failed); continue;
                }
                if (this.owner?.Key == null) { this.SendError(item, FriendListStatus.NotLoggedIn); continue; }
                if (request.RequestId != null && (request.ExpectedOwnerKey != this.owner.Key || request.ExpectedOwnerEpoch != this.epoch)) {
                    this.SendError(item, FriendListStatus.IdentityChanged); continue;
                }
                if (this.cached != null && now - this.cached.CapturedAt < TimeSpan.FromSeconds(5)) { this.SendSnapshot(item); continue; }
                if (!this.pending.ContainsKey(item.ClientId) && this.pending.Count >= 256) { this.SendError(item, FriendListStatus.Busy); continue; }
                this.pending[item.ClientId] = item;
            }
            if (this.pending.Count == 0 || this.refreshing || now < this.nextRefresh) return;
            this.refreshing = true;
            this.deadline = now.AddSeconds(10); this.nextRefresh = now.AddSeconds(5);
            var failure = this.reader.BeginRefresh();
            if (failure.HasValue) { this.refreshing = false; this.CompleteError(failure.Value); }
        }

        private void SendSnapshot(FriendRequest request) {
            if (request.Cancellation.IsCancellationRequested || this.cached == null) return;
            try {
                if (request.Request.RequestId == null) {
                    var legacy = new ServerPlayerList(PlayerListType.Friend, this.cached.Players);
                    if (legacy.Encode().Length > FriendListProtocol.MaxPageBytes) { this.SendError(request, FriendListStatus.Failed); return; }
                    this.reply(request, legacy);
                } else {
                    foreach (var page in FriendListProtocol.Pages(this.cached, request.Request.RequestId)) this.reply(request, page);
                }
            } catch (ArgumentException) { this.SendError(request, FriendListStatus.Failed); }
        }

        private void CompleteError(FriendListStatus status) {
            this.LastStatus = status;
            foreach (var item in this.pending.Values) this.SendError(item, status);
            this.pending.Clear();
        }

        private void SendError(FriendRequest item, FriendListStatus status) {
            if (!item.Cancellation.IsCancellationRequested)
                this.reply(item, FriendListProtocol.Error(item.Request.RequestId, this.owner, this.epoch, status));
        }

        public void Dispose() { this.requests.Writer.TryComplete(); this.reader.Dispose(); }
    }
}
