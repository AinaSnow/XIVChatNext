using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVChatPlugin {
    /// <summary>
    /// Per-source limits for unauthenticated work, keyed by remote IP address. Relay sessions are not gated because
    /// the relay has already authenticated them. Trusted devices are never limited after the handshake.
    /// </summary>
    internal sealed class ConnectionGate {
        internal const int MaxHandshakesPerSource = 4;
        internal const int MaxTrustRequestsPerWindow = 3;
        internal static readonly TimeSpan TrustWindow = TimeSpan.FromMinutes(1);

        private readonly object _gate = new();
        private readonly Func<long> _clock;
        private readonly Dictionary<string, int> _handshakes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingTrust = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<long>> _trustHistory = new(StringComparer.Ordinal);

        internal ConnectionGate(Func<long>? clock = null) {
            this._clock = clock ?? (() => Environment.TickCount64);
        }

        internal bool TryBeginHandshake(string source) {
            lock (this._gate) {
                this._handshakes.TryGetValue(source, out var count);
                if (count >= MaxHandshakesPerSource) return false;
                this._handshakes[source] = count + 1;
                return true;
            }
        }

        internal void EndHandshake(string source) {
            lock (this._gate) {
                if (!this._handshakes.TryGetValue(source, out var count)) return;
                if (count <= 1) this._handshakes.Remove(source);
                else this._handshakes[source] = count - 1;
            }
        }

        /// <summary>One pending approval per source, and a few per minute, so an untrusted host cannot flood prompts.</summary>
        internal bool TryBeginTrustRequest(string source) {
            lock (this._gate) {
                if (this._pendingTrust.Contains(source)) return false;
                var now = this._clock();
                if (this._trustHistory.Count >= 256) this.PruneTrustHistory(now);
                if (!this._trustHistory.TryGetValue(source, out var history)) this._trustHistory[source] = history = new Queue<long>();
                while (history.Count > 0 && now - history.Peek() >= (long)TrustWindow.TotalMilliseconds) history.Dequeue();
                if (history.Count >= MaxTrustRequestsPerWindow) return false;
                history.Enqueue(now);
                this._pendingTrust.Add(source);
                return true;
            }
        }

        internal void EndTrustRequest(string source) {
            lock (this._gate) this._pendingTrust.Remove(source);
        }

        private void PruneTrustHistory(long now) {
            var stale = new List<string>();
            foreach (var (source, history) in this._trustHistory)
                if (!this._pendingTrust.Contains(source) && (history.Count == 0 || now - history.Last() >= (long)TrustWindow.TotalMilliseconds))
                    stale.Add(source);
            foreach (var source in stale) this._trustHistory.Remove(source);
        }

        internal (int Handshakes, int PendingTrust) Usage {
            get { lock (this._gate) return (this._handshakes.Count, this._pendingTrust.Count); }
        }
    }
}
