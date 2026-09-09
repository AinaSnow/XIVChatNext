using System;
using System.Collections.Generic;

namespace XIVChatPlugin {
    /// <summary>Framework-thread LRU cache. Missing values are cached too.</summary>
    internal sealed class BoundedCache<TKey, TValue> where TKey : notnull {
        private readonly int capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> entries = new();
        private readonly LinkedList<(TKey Key, TValue Value)> order = new();
        internal int Count => this.entries.Count;
        internal long Hits { get; private set; }
        internal long Misses { get; private set; }
        internal BoundedCache(int capacity) {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }
        internal TValue Get(TKey key, Func<TKey, TValue> load) {
            if (this.entries.TryGetValue(key, out var node)) {
                this.Hits++; this.order.Remove(node); this.order.AddLast(node); return node.Value.Value;
            }
            this.Misses++;
            var value = load(key);
            if (this.entries.Count == this.capacity) {
                this.entries.Remove(this.order.First!.Value.Key); this.order.RemoveFirst();
            }
            this.entries[key] = this.order.AddLast((key, value));
            return value;
        }
        internal void Clear() { this.entries.Clear(); this.order.Clear(); }
    }
}
