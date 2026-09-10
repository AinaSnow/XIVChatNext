using System;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    /// <summary>UI-thread state. A snapshot becomes visible only when every page has passed validation.</summary>
    public sealed class FriendListSession {
        public string Source { get; private set; } = "";
        public string? OwnerKey { get; private set; }
        public string? OwnerEpoch { get; private set; }
        public bool Supported { get; private set; }
        public int Version { get; private set; }
        public ServerPlayerList? Snapshot { get; private set; }
        public bool IsStale { get; private set; } = true;
        public FriendListStatus? Status { get; private set; }
        public bool Manual { get; private set; }
        public event Action? Changed;
        private FriendListAssembler? pending;
        public string? RequestId => this.pending?.RequestId;

        public bool SetContext(string source, string? owner, string? epoch, bool supported) {
            if (source == this.Source && owner == this.OwnerKey && epoch == this.OwnerEpoch && supported == this.Supported) return false;
            if (source != this.Source || (owner != null && owner != this.Snapshot?.Owner?.Key)) this.Snapshot = null;
            this.Source = source; this.OwnerKey = owner; this.OwnerEpoch = epoch; this.Supported = supported;
            this.Version++; this.pending = null; this.IsStale = true; this.Status = null; this.Manual = false;
            this.Changed?.Invoke();
            return true;
        }

        public void Restore(int version, ServerPlayerList? snapshot) {
            if (version != this.Version || !this.IsStale || this.Snapshot != null || snapshot?.Owner?.Key != this.OwnerKey ||
                snapshot?.Type != PlayerListType.Friend || snapshot.Status != FriendListStatus.Success ||
                snapshot.PageIndex != 0 || snapshot.PageCount != 1 || !FriendListProtocol.ValidPlayers(snapshot.Players)) return;
            this.Snapshot = snapshot;
            this.Changed?.Invoke();
        }

        public ClientPlayerList? Begin(bool manual) {
            if (!this.Supported || this.OwnerKey == null || string.IsNullOrEmpty(this.OwnerEpoch) || this.pending != null) return null;
            var id = Guid.NewGuid().ToString("N");
            this.pending = new FriendListAssembler(id, this.OwnerKey, this.OwnerEpoch);
            this.Manual = manual; this.Status = null;
            this.Changed?.Invoke();
            return new ClientPlayerList { Type = PlayerListType.Friend, RequestId = id, ExpectedOwnerKey = this.OwnerKey, ExpectedOwnerEpoch = this.OwnerEpoch };
        }

        public ServerPlayerList? Add(ServerPlayerList page) {
            if (this.pending == null) return null;
            var completed = this.pending.Add(page);
            if (this.pending.Failure is { } failure) this.Fail(this.pending.RequestId, failure);
            if (completed == null) return null;
            this.pending = null; this.Snapshot = completed; this.IsStale = false; this.Status = FriendListStatus.Success;
            this.Changed?.Invoke();
            return completed;
        }

        public bool Fail(string requestId, FriendListStatus status) {
            if (this.pending?.RequestId != requestId) return false;
            this.pending = null; this.IsStale = true; this.Status = status;
            this.Changed?.Invoke();
            return true;
        }
    }
}
