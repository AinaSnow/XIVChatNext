using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    internal sealed record GameCommandContext(string? OwnerKey, string Epoch, long ChannelRevision);
    internal sealed record GameCommand(Guid ClientId, string? RequestId, GameCommandContext Context,
        string? Text, InputChannel? Channel, CancellationToken Cancellation);

    /// <summary>Network threads only enqueue managed values. The framework thread validates and executes them.</summary>
    internal sealed class GameCommandQueue {
        internal const int MaxCount = 128;
        internal const int MaxPerClient = 32;
        internal const int MaxBytes = 64 * 1024;
        private readonly object gate = new();
        private readonly LinkedList<GameCommand> commands = new();
        private readonly Dictionary<Guid, int> perClient = new();
        private int bytes;
        internal (int Count, int Bytes) Usage { get { lock (this.gate) return (this.commands.Count, this.bytes); } }

        internal bool TryEnqueue(IReadOnlyList<GameCommand> batch) {
            if (batch.Count == 0 || batch.Count > MaxPerClient) return false;
            var client = batch[0].ClientId;
            if (batch.Any(c => c.ClientId != client || c.Cancellation.IsCancellationRequested)) return false;
            var size = batch.Sum(Size);
            lock (this.gate) {
                this.RemoveWhere(c => c.Cancellation.IsCancellationRequested);
                var owned = this.perClient.GetValueOrDefault(client);
                if (this.commands.Count + batch.Count > MaxCount || owned + batch.Count > MaxPerClient || size > MaxBytes - this.bytes) return false;
                foreach (var command in batch) this.commands.AddLast(command);
                this.bytes += size;
                this.perClient[client] = owned + batch.Count;
                return true;
            }
        }

        internal GameCommand? Peek() { lock (this.gate) return this.commands.First?.Value; }
        internal GameCommand? Take(GameCommand expected) {
            lock (this.gate) {
                if (this.commands.First == null || !ReferenceEquals(this.commands.First.Value, expected)) return null;
                var command = this.commands.First.Value;
                this.Remove(this.commands.First);
                return command;
            }
        }
        internal void CancelClient(Guid client) { lock (this.gate) this.RemoveWhere(c => c.ClientId == client); }
        internal GameCommand[] Clear() {
            lock (this.gate) {
                var removed = this.commands.ToArray();
                this.commands.Clear(); this.perClient.Clear(); this.bytes = 0;
                return removed;
            }
        }
        internal static CommandFailure? Validate(GameCommand command, GameCommandContext current) {
            if (command.Cancellation.IsCancellationRequested) return CommandFailure.Disconnected;
            if (current.OwnerKey == null) return CommandFailure.NotLoggedIn;
            if (current.OwnerKey != command.Context.OwnerKey || current.Epoch != command.Context.Epoch) return CommandFailure.IdentityChanged;
            if (command.Channel == null && current.ChannelRevision != command.Context.ChannelRevision) return CommandFailure.ChannelChanged;
            return null;
        }
        internal static CommandFailure? ValidateRequest(GameCommandContext context, bool guarded, string? request,
            string? owner, string? epoch, long? channelRevision, bool checkChannel) {
            if (request?.Length > 64 || owner?.Length > 128 || epoch?.Length > 64) return CommandFailure.InvalidRequest;
            if (context.OwnerKey == null) return CommandFailure.NotLoggedIn;
            if (checkChannel && context.ChannelRevision <= 0) return CommandFailure.ChannelChanged;
            if (guarded && (string.IsNullOrEmpty(request) || owner != context.OwnerKey || epoch != context.Epoch)) return CommandFailure.IdentityChanged;
            if (owner != null && (owner != context.OwnerKey || epoch != context.Epoch)) return CommandFailure.IdentityChanged;
            if (checkChannel && ((channelRevision.HasValue && channelRevision != context.ChannelRevision) || (guarded && channelRevision == null)))
                return CommandFailure.ChannelChanged;
            return null;
        }
        private static int Size(GameCommand command) => command.Text == null ? 1 : Encoding.UTF8.GetByteCount(command.Text);
        private void Remove(LinkedListNode<GameCommand> node) {
            this.bytes -= Size(node.Value);
            if (--this.perClient[node.Value.ClientId] == 0) this.perClient.Remove(node.Value.ClientId);
            this.commands.Remove(node);
        }
        private void RemoveWhere(Func<GameCommand, bool> predicate) {
            var node = this.commands.First;
            while (node != null) { var next = node.Next; if (predicate(node.Value)) this.Remove(node); node = next; }
        }
    }
}
