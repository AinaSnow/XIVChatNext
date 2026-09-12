using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public partial class Connection {
        private sealed record PendingCard(ClientGameCard Request, TaskCompletionSource<ServerGameCard> Completion);
        private readonly ConcurrentDictionary<string, PendingCard> pendingCards = new();
        private readonly SemaphoreSlim cardRequests = new(4);
        public bool SupportsGameCards => this.capabilities?.GameCards == true;
        public async Task<ServerGameCard> RequestCardAsync(ClientGameCard request, CancellationToken token) {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, this.cancel.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(50));
            await this.cardRequests.WaitAsync(lifetime.Token);
            request.RequestId = Guid.NewGuid().ToString("N");
            try {
                if (!this.SupportsGameCards || !ReferenceEquals(this.app.Connection, this) || !request.Valid) throw new InvalidOperationException("Game card request unavailable.");
                var completion = new TaskCompletionSource<ServerGameCard>(TaskCreationOptions.RunContinuationsAsynchronously);
                this.pendingCards[request.RequestId] = new(request, completion);
                if (!this.QueuePacket(request.Encode())) throw new InvalidOperationException("Game card queue is full.");
                return await completion.Task.WaitAsync(lifetime.Token);
            } finally { this.pendingCards.TryRemove(request.RequestId, out _); this.cardRequests.Release(); }
        }
        private void ReceiveCard(ServerGameCard reply) {
            if (!reply.Valid) return;
            if (reply.Query == CardQuery.Equipment && reply.Equipment != null)
                this.DispatchIfCurrent(() => this.app.Cards.ObserveEquipment(this, reply.Equipment));
            if (!this.pendingCards.TryGetValue(reply.RequestId, out var pending)) return;
            var request = pending.Request;
            if (request.Query != reply.Query || request.Id != reply.Id || request.ItemKind != reply.ItemKind ||
                (reply.Status == CardStatus.Success && (request.Page != reply.Page ||
                    (!string.IsNullOrEmpty(request.DataScope) && reply.DataScope != request.DataScope)))) return;
            pending.Completion.TrySetResult(reply);
        }
    }
}
