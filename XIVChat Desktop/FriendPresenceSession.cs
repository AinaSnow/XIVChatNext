using System;
using System.Collections.Generic;
using System.Linq;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    // Ephemeral: disk snapshots never establish fresh online status for a new connection.
    public sealed class FriendPresenceSession {
        private readonly Dictionary<ulong, ServerFriendPresence> values = new();
        private readonly Dictionary<ulong, DateTime> attempted = new();
        private string source = "", owner = "", epoch = "";
        private bool supported;
        private ClientFriendPresence? pending;
        private DateTime deadline;
        public event Action? Changed;
        public void SetContext(string source, string? owner, string? epoch, bool supported) {
            if (this.source == source && this.owner == (owner ?? "") && this.epoch == (epoch ?? "") && this.supported == supported) return;
            this.source = source; this.owner = owner ?? ""; this.epoch = epoch ?? ""; this.supported = supported;
            this.pending = null; this.values.Clear(); this.attempted.Clear(); this.Changed?.Invoke();
        }
        public ServerFriendPresence? Get(ulong cid) => this.values.TryGetValue(cid, out var value) ? value : null;
        public static bool Fresh(ServerFriendPresence? value, DateTime now) => value?.Status == FriendListStatus.Success && value.CheckedAt <= now.AddSeconds(5) && now - value.CheckedAt < TimeSpan.FromMinutes(1);
        public bool IsPending(ulong cid) => this.pending?.ContentId == cid;
        public ClientFriendPresence? Begin(ulong cid, DateTime now) {
            this.Expire(now);
            if (!this.supported || this.owner.Length == 0 || this.epoch.Length == 0 || cid == 0 || this.pending != null ||
                (this.attempted.TryGetValue(cid, out var last) && now - last < TimeSpan.FromSeconds(30))) return null;
            if (this.attempted.Count >= FriendListProtocol.MaxFriends) { this.attempted.Clear(); this.values.Clear(); }
            this.attempted[cid] = now; this.deadline = now.AddSeconds(15);
            var request = new ClientFriendPresence { RequestId = Guid.NewGuid().ToString("N"), OwnerKey = this.owner, OwnerEpoch = this.epoch, ContentId = cid };
            this.pending = request; this.Changed?.Invoke(); return request;
        }
        public bool Add(ServerFriendPresence value, DateTime now) {
            if (this.pending == null || value.RequestId != this.pending.RequestId || value.ContentId != this.pending.ContentId || value.OwnerKey != this.owner || value.OwnerEpoch != this.epoch) return false;
            if (!Enum.IsDefined(value.Presence) || !Enum.IsDefined(value.Status) || (value.Status == FriendListStatus.Success && (value.CheckedAt == default || value.CheckedAt > now.AddSeconds(5)))) return false;
            this.pending = null; this.values[value.ContentId] = value; this.Changed?.Invoke(); return true;
        }
        public void Expire(DateTime now) {
            if (this.pending == null || now < this.deadline) return;
            var request = this.pending; this.pending = null;
            this.values[request.ContentId] = new ServerFriendPresence { ContentId = request.ContentId, Status = FriendListStatus.TimedOut };
            this.Changed?.Invoke();
        }
    }
}
