using System;
using System.Collections.Generic;
using System.Linq;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    internal sealed class MessageBacklog {
        private readonly object gate = new();
        private readonly LinkedList<(ServerMessage Message, int Bytes)> messages = new();
        private readonly string serviceId;
        private readonly string runId;
        private int capacity;
        private long byteCapacity;
        private long bytes;
        private long sequence;
        internal MessageBacklog(string serviceId, string runId) { this.serviceId = serviceId; this.runId = runId; }
        internal (int Count, long Bytes) Usage { get { lock (this.gate) return (this.messages.Count, this.bytes); } }
        internal bool Enabled { get { lock (this.gate) return this.capacity > 0 && this.byteCapacity > 0; } }
        internal void Configure(bool enabled, int capacity, long byteCapacity) {
            lock (this.gate) {
                this.capacity = enabled ? Math.Max(0, capacity) : 0;
                this.byteCapacity = enabled ? Math.Max(0, byteCapacity) : 0;
                this.Trim();
            }
        }
        internal byte[] Record(ServerMessage message) {
            lock (this.gate) {
                message.ServiceId = this.serviceId; message.RunId = this.runId;
                message.Sequence = ++this.sequence;
                message.MessageId = $"{this.serviceId}:{this.runId}:{message.Sequence}";
                var encoded = message.Encode();
                if (this.capacity > 0 && encoded.Length <= this.byteCapacity) {
                    this.messages.AddLast((message, encoded.Length)); this.bytes += encoded.Length;
                    this.Trim();
                }
                return encoded;
            }
        }
        internal (ServerMessage[] Messages, long Latest) Snapshot() {
            lock (this.gate) return (this.messages.Select(m => m.Message).ToArray(), this.sequence);
        }
        private void Trim() {
            while (this.messages.First != null && (this.messages.Count > this.capacity || this.bytes > this.byteCapacity)) {
                this.bytes -= this.messages.First.Value.Bytes;
                this.messages.RemoveFirst();
            }
        }
    }
}
