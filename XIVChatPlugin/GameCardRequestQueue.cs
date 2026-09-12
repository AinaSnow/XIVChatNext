using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XIVChatCommon.Message.Client;

namespace XIVChatPlugin {
    internal sealed record GameCardRequest(Guid ClientId, ClientGameCard Message, CancellationToken Cancellation, DateTime EnqueuedAt);
    internal sealed class GameCardRequestQueue {
        private readonly object gate = new();
        private readonly Queue<GameCardRequest> pending = new();
        private readonly Dictionary<Guid, int> outstanding = new();
        internal bool Enqueue(GameCardRequest request) {
            lock (this.gate) {
                if (!request.Message.Valid || request.Cancellation.IsCancellationRequested || this.outstanding.Values.Sum() >= 32 ||
                    this.outstanding.GetValueOrDefault(request.ClientId) >= 4) return false;
                this.outstanding[request.ClientId] = this.outstanding.GetValueOrDefault(request.ClientId) + 1;
                this.pending.Enqueue(request); return true;
            }
        }
        internal GameCardRequest? Take() { lock (this.gate) return this.pending.TryDequeue(out var request) ? request : null; }
        internal void Complete(GameCardRequest request) {
            lock (this.gate) {
                int remaining = this.outstanding.GetValueOrDefault(request.ClientId) - 1;
                if (remaining <= 0) this.outstanding.Remove(request.ClientId); else this.outstanding[request.ClientId] = remaining;
            }
        }
        internal void Clear() { lock (this.gate) { this.pending.Clear(); this.outstanding.Clear(); } }
    }
}
