using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using XIVChatCommon.Message;

namespace XIVChatPlugin {
    internal sealed record PresenceRequest(Guid ClientId, ClientFriendPresence Request, CancellationToken Cancellation);
    internal sealed record PresenceReadResult(string Epoch, ulong ContentId, PresenceState Presence, FriendListStatus Status);
    internal interface IFriendPresenceReader : IDisposable {
        void SetContext(CharacterIdentity? owner, string epoch);
        FriendListStatus? Begin(ulong contentId);
        PresenceReadResult? TakeResult();
        void Cancel();
    }

    // Single framework-thread lane; network callers only enqueue bounded managed requests.
    internal sealed class FriendPresenceCoordinator : IDisposable {
        private readonly IFriendPresenceReader reader;
        private readonly Action<PresenceRequest, ServerFriendPresence> reply;
        private readonly Channel<PresenceRequest> queue = Channel.CreateBounded<PresenceRequest>(256);
        private readonly Dictionary<Guid, PresenceRequest> pending = new();
        private readonly Dictionary<ulong, (PresenceState State, DateTime Time)> cache = new();
        private CharacterIdentity? owner;
        private string epoch = "";
        private ulong active;
        private DateTime deadline, nextRequest;
        internal FriendPresenceCoordinator(IFriendPresenceReader reader, Action<PresenceRequest, ServerFriendPresence> reply) { this.reader = reader; this.reply = reply; }
        internal bool Enqueue(PresenceRequest request) => request.Request.Valid && this.queue.Writer.TryWrite(request);
        internal void Tick(CharacterIdentity? owner, string epoch, DateTime now) {
            if (this.owner?.Key != owner?.Key || this.epoch != epoch) {
                foreach (var item in this.pending.Values) this.Send(item, FriendListStatus.IdentityChanged);
                this.pending.Clear(); this.cache.Clear(); this.active = 0; this.nextRequest = DateTime.MinValue;
                this.owner = owner; this.epoch = epoch; this.reader.SetContext(owner, epoch);
            }
            foreach (var id in this.pending.Where(x => x.Value.Cancellation.IsCancellationRequested).Select(x => x.Key).ToArray()) this.pending.Remove(id);
            var result = this.reader.TakeResult();
            if (this.active != 0 && result?.ContentId == this.active && result.Epoch == this.epoch) {
                if (result.Status == FriendListStatus.Success) {
                    if (this.cache.Count >= FriendListProtocol.MaxFriends) this.cache.Clear();
                    this.cache[this.active] = (result.Presence, now);
                }
                this.Complete(result.Status, result.Presence, now);
            }
            if (this.active != 0 && now >= this.deadline) { this.reader.Cancel(); this.Complete(FriendListStatus.TimedOut); }
            for (int i = 0; i < 64 && this.queue.Reader.TryRead(out var item); i++) {
                if (item.Cancellation.IsCancellationRequested) continue;
                if (this.owner?.Key == null) { this.Send(item, FriendListStatus.NotLoggedIn); continue; }
                if (item.Request.OwnerKey != this.owner.Key || item.Request.OwnerEpoch != this.epoch) { this.Send(item, FriendListStatus.IdentityChanged); continue; }
                if (this.cache.TryGetValue(item.Request.ContentId, out var cached) && now - cached.Time < TimeSpan.FromSeconds(30)) {
                    this.Send(item, FriendListStatus.Success, cached.State, cached.Time); continue;
                }
                if (this.pending.ContainsKey(item.ClientId) || this.pending.Count >= 256) { this.Send(item, FriendListStatus.Busy); continue; }
                this.pending[item.ClientId] = item;
            }
            if (this.active != 0 || this.pending.Count == 0 || now < this.nextRequest) return;
            this.active = this.pending.Values.First().Request.ContentId;
            this.deadline = now.AddSeconds(10); this.nextRequest = now.AddSeconds(5);
            if (this.reader.Begin(this.active) is { } failure) this.Complete(failure);
        }
        private void Complete(FriendListStatus status, PresenceState presence = PresenceState.Unknown, DateTime time = default) {
            foreach (var item in this.pending.Values.Where(x => x.Request.ContentId == this.active).ToArray()) {
                this.Send(item, status, presence, time); this.pending.Remove(item.ClientId);
            }
            this.active = 0;
        }
        private void Send(PresenceRequest item, FriendListStatus status, PresenceState presence = PresenceState.Unknown, DateTime time = default) {
            if (!item.Cancellation.IsCancellationRequested) this.reply(item, new ServerFriendPresence {
                RequestId = item.Request.RequestId, OwnerKey = item.Request.OwnerKey, OwnerEpoch = item.Request.OwnerEpoch,
                ContentId = item.Request.ContentId, Status = status, Presence = presence, CheckedAt = time,
            });
        }
        public void Dispose() { this.queue.Writer.TryComplete(); this.reader.Dispose(); }
    }
}
