using System;
using System.Collections.Generic;
using System.Threading.Channels;

namespace XIVChatPlugin {
    internal sealed record PendingDutyEvent(uint DataId, string Name, DateTime At, string OwnerKey, string Epoch);

    // Dalamud raises CfPop on a worker task. Only immutable managed data crosses this queue.
    internal sealed class GameEventQueue {
        private readonly Channel<PendingDutyEvent> pending = Channel.CreateBounded<PendingDutyEvent>(8);
        internal bool TryEnqueue(uint id, string name, DateTime at, GameCommandContext context) => context.OwnerKey != null &&
            pending.Writer.TryWrite(new PendingDutyEvent(id, name.Length <= 256 ? name : name[..256], at, context.OwnerKey, context.Epoch));
        internal IReadOnlyList<PendingDutyEvent> Drain(string? ownerKey, string epoch) {
            var ready = new List<PendingDutyEvent>();
            for (var i = 0; i < 8 && pending.Reader.TryRead(out var item); i++)
                if (item.OwnerKey == ownerKey && item.Epoch == epoch) ready.Add(item);
            return ready;
        }
        internal void Clear() { for (var i = 0; i < 8 && pending.Reader.TryRead(out _); i++) { } }
        internal void Complete() { pending.Writer.TryComplete(); Clear(); }
    }
}
