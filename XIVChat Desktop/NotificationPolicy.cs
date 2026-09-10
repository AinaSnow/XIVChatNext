using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    public enum NotificationKind { Tell, Keyword, DutyReady, ConnectionLost, LoginLogout, TerritoryChanged, Test }
    public enum NotificationTargetKind { Conversation, Message, Event }

    public sealed class NotificationOptions {
        public bool DutyReady { get; set; } = true;
        public bool Tell { get; set; } = true;
        public bool Keyword { get; set; } = true;
        public bool ConnectionLost { get; set; } = true;
        public bool LoginLogout { get; set; }
        public bool TerritoryChanged { get; set; }
        public bool Sound { get; set; } = true;
        public bool DoNotDisturb { get; set; }
        public bool QuietHours { get; set; }
        public TimeSpan QuietStart { get; set; } = TimeSpan.FromHours(22);
        public TimeSpan QuietEnd { get; set; } = TimeSpan.FromHours(8);

        public bool Allows(NotificationKind kind) => kind switch {
            NotificationKind.Tell => Tell, NotificationKind.Keyword => Keyword,
            NotificationKind.DutyReady => DutyReady, NotificationKind.ConnectionLost => ConnectionLost,
            NotificationKind.LoginLogout => LoginLogout, NotificationKind.TerritoryChanged => TerritoryChanged,
            NotificationKind.Test => true, _ => false,
        };
        public bool IsQuiet(DateTime localNow) {
            if (DoNotDisturb) return true;
            if (!QuietHours) return false;
            var time = localNow.TimeOfDay;
            return QuietStart == QuietEnd || (QuietStart < QuietEnd
                ? time >= QuietStart && time < QuietEnd : time >= QuietStart || time < QuietEnd);
        }
        public bool ValidTimes => QuietStart >= TimeSpan.Zero && QuietStart < TimeSpan.FromDays(1) &&
            QuietEnd >= TimeSpan.Zero && QuietEnd < TimeSpan.FromDays(1);
    }

    public sealed record NotificationTarget(NotificationTargetKind Kind, string Source, string OwnerKey,
        string? RecordId = null, CharacterIdentity? Peer = null, ushort? Channel = null) {
        [JsonIgnore] public bool Valid => Enum.IsDefined(Kind) && Source is { Length: > 0 and <= 256 } && OwnerKey is { Length: > 0 and <= 192 } &&
            (RecordId == null || RecordId.Length <= 512) && (Kind != NotificationTargetKind.Event || !string.IsNullOrWhiteSpace(RecordId)) &&
            (Peer == null || Peer.IsComplete && Peer.Name.Length <= 80 && Peer.HomeWorld is { Length: <= 80 }) &&
            (Kind != NotificationTargetKind.Conversation || ConversationIdentity.PeerKey(Peer) != null);
        [JsonIgnore] public string GroupKey => Source + "/" + OwnerKey + "/" + Kind + "/" +
            (Kind == NotificationTargetKind.Conversation ? ConversationIdentity.PeerKey(Peer) : Kind == NotificationTargetKind.Message ? Channel?.ToString() : RecordId);
        public string ToArgument() => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));
        public static NotificationTarget? Parse(string? value) {
            if (string.IsNullOrEmpty(value) || value.Length > 4096) return null;
            try {
                var target = JsonSerializer.Deserialize<NotificationTarget>(Convert.FromBase64String(value));
                return target?.Valid == true ? target : null;
            } catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException) { return null; }
        }
    }

    public sealed record NotificationCandidate(string Id, NotificationKind Kind, NotificationTarget Target,
        string Title, string Text, DateTime Timestamp, DateTime? ExpiresAt = null,
        string? ConnectionId = null, string? OwnerEpoch = null);
    public sealed record NotificationDelivery(NotificationCandidate Candidate, int Count, bool Sound, string Tag);

    /// <summary>UI-independent notification decisions; all clock and reading state is supplied by the caller.</summary>
    public sealed class NotificationPolicy {
        private sealed record Pending(NotificationCandidate Candidate, int Count, DateTime Due);
        private readonly Dictionary<string, Pending> pending = new();
        private readonly HashSet<string> seen = new();
        private readonly Queue<string> seenOrder = new();
        private readonly Dictionary<string, DateTime> lastSent = new();
        private DateTime lastSound = DateTime.MinValue;
        public int PendingCount => pending.Count;

        public bool Enqueue(NotificationCandidate candidate, NotificationOptions options, DateTime utcNow, DateTime localNow, bool reading = false) {
            if (!candidate.Target.Valid || string.IsNullOrEmpty(candidate.Id)) return false;
            var id = candidate.Target.Source + "/" + candidate.Id;
            if (!seen.Add(id)) return false;
            seenOrder.Enqueue(id);
            while (seenOrder.Count > 4096) seen.Remove(seenOrder.Dequeue());
            // Even suppressed inputs are consumed; changing settings never replays old notifications.
            if (!Eligible(candidate, options, utcNow, localNow) || reading && IsMessage(candidate)) return false;
            var group = candidate.Kind + "/" + candidate.Target.GroupKey;
            if (pending.TryGetValue(group, out var current)) {
                pending[group] = current with { Candidate = candidate, Count = current.Count + 1 };
                return true;
            }
            if (pending.Count >= 128) pending.Remove(pending.MinBy(p => p.Value.Due).Key);
            var due = IsMessage(candidate) ? utcNow.AddSeconds(2) : utcNow;
            if (IsMessage(candidate) && lastSent.TryGetValue(group, out var sent) && due < sent.AddSeconds(10)) due = sent.AddSeconds(10);
            pending[group] = new Pending(candidate, 1, due);
            return true;
        }

        public IReadOnlyList<NotificationDelivery> Drain(NotificationOptions options, DateTime utcNow, DateTime localNow,
            Func<NotificationTarget, bool> reading, Func<NotificationCandidate, bool>? applicable = null) {
            var deliveries = new List<NotificationDelivery>();
            foreach (var pair in pending.OrderBy(p => p.Value.Due).ToArray()) {
                var item = pair.Value;
                if (!Eligible(item.Candidate, options, utcNow, localNow) ||
                    IsMessage(item.Candidate) && reading(item.Candidate.Target) || applicable?.Invoke(item.Candidate) == false) {
                    pending.Remove(pair.Key); continue;
                }
                if (item.Due > utcNow || deliveries.Count >= 32) continue;
                pending.Remove(pair.Key);
                var sound = options.Sound && (item.Candidate.Kind == NotificationKind.DutyReady || utcNow - lastSound >= TimeSpan.FromSeconds(5));
                if (sound) lastSound = utcNow;
                lastSent[pair.Key] = utcNow;
                while (lastSent.Count > 256) lastSent.Remove(lastSent.MinBy(p => p.Value).Key);
                var tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Key)))[..16];
                deliveries.Add(new NotificationDelivery(item.Candidate, item.Count, sound, tag));
            }
            return deliveries;
        }

        private static bool IsMessage(NotificationCandidate candidate) => candidate.Kind is NotificationKind.Tell or NotificationKind.Keyword;
        private static bool Eligible(NotificationCandidate candidate, NotificationOptions options, DateTime utcNow, DateTime localNow) =>
            options.Allows(candidate.Kind) && !options.IsQuiet(localNow) && candidate.Timestamp <= utcNow.AddSeconds(30) &&
            candidate.Timestamp >= utcNow.AddMinutes(-2) && (candidate.ExpiresAt == null || candidate.ExpiresAt > utcNow);
        public void ClearPending() => pending.Clear();
    }
}
