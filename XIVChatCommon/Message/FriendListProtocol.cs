using System;
using System.Collections.Generic;
using System.Linq;
using XIVChatCommon.Message.Server;

namespace XIVChatCommon.Message {
    public enum FriendListStatus { Success, NotLoggedIn, Unavailable, Busy, TimedOut, IdentityChanged, Failed }

    public static class FriendListProtocol {
        public const int MaxFriends = 200;
        public const int MaxPageBytes = 120 * 1024;
        public const int PageSize = 32;
        public const int MaxPages = (MaxFriends + PageSize - 1) / PageSize;

        public static bool ValidPlayers(Player[]? players) => players != null && players.Length <= MaxFriends &&
            players.All(p => p != null && p.ContentId != 0 && p.Name != null && p.Name.Length <= 64 &&
                p.IdentityUnavailable == (p.HomeWorld == 0 || string.IsNullOrWhiteSpace(p.Name))) &&
            players.Select(p => p.ContentId).Distinct().Count() == players.Length;

        public static ServerPlayerList Error(string? requestId, CharacterIdentity? owner, string? epoch, FriendListStatus status) =>
            new(PlayerListType.Friend, Array.Empty<Player>()) { RequestId = requestId, Owner = owner, OwnerEpoch = epoch, Status = status };

        public static ServerPlayerList[] Pages(ServerPlayerList snapshot, string? requestId) {
            if (!ValidPlayers(snapshot.Players) || snapshot.Owner?.Key == null || string.IsNullOrEmpty(snapshot.OwnerEpoch) ||
                string.IsNullOrEmpty(snapshot.SnapshotId) || snapshot.Status != FriendListStatus.Success)
                throw new ArgumentException("Invalid friend snapshot.", nameof(snapshot));
            var count = Math.Max(1, (snapshot.Players.Length + PageSize - 1) / PageSize);
            var pages = new ServerPlayerList[count];
            for (int i = 0; i < count; i++) {
                pages[i] = new ServerPlayerList(PlayerListType.Friend, snapshot.Players.Skip(i * PageSize).Take(PageSize).ToArray()) {
                    RequestId = requestId, Owner = snapshot.Owner, OwnerEpoch = snapshot.OwnerEpoch,
                    SnapshotId = snapshot.SnapshotId, CapturedAt = snapshot.CapturedAt, PageIndex = i, PageCount = count,
                };
                if (pages[i].Encode().Length > MaxPageBytes) throw new ArgumentException("Friend page exceeds packet budget.");
            }
            return pages;
        }
    }

    /// <summary>One ordered response, tied to a connection's request and login episode. No partial snapshot escapes.</summary>
    public sealed class FriendListAssembler {
        public string RequestId { get; }
        public string OwnerKey { get; }
        public string OwnerEpoch { get; }
        public FriendListStatus? Failure { get; private set; }
        private ServerPlayerList? first;
        private readonly List<Player> players = new();
        private int nextPage;
        private bool finished;

        public FriendListAssembler(string requestId, string ownerKey, string ownerEpoch) {
            this.RequestId = requestId; this.OwnerKey = ownerKey; this.OwnerEpoch = ownerEpoch;
        }

        public ServerPlayerList? Add(ServerPlayerList page) {
            if (this.finished || page.RequestId != this.RequestId || page.Type != PlayerListType.Friend) return null;
            // Errors can describe a different/current owner; they must still terminate this request.
            if (page.Status != FriendListStatus.Success) { this.Fail(page.Status); return null; }
            if (page.Owner?.Key != this.OwnerKey || page.OwnerEpoch != this.OwnerEpoch ||
                string.IsNullOrEmpty(page.SnapshotId) || page.SnapshotId.Length > 64 || page.PageIndex != this.nextPage ||
                page.PageCount < 1 || page.PageCount > FriendListProtocol.MaxPages || page.PageIndex >= page.PageCount ||
                page.Players == null || page.Players.Length > FriendListProtocol.PageSize ||
                this.players.Count + page.Players.Length > FriendListProtocol.MaxFriends || !FriendListProtocol.ValidPlayers(page.Players)) {
                this.Fail(FriendListStatus.Failed); return null;
            }
            this.first ??= page;
            if (page.SnapshotId != this.first.SnapshotId || page.PageCount != this.first.PageCount || page.CapturedAt != this.first.CapturedAt) {
                this.Fail(FriendListStatus.Failed); return null;
            }
            this.players.AddRange(page.Players);
            this.nextPage++;
            if (this.nextPage < page.PageCount) return null;
            if (!FriendListProtocol.ValidPlayers(this.players.ToArray())) { this.Fail(FriendListStatus.Failed); return null; }
            this.finished = true;
            return new ServerPlayerList(PlayerListType.Friend, this.players.ToArray()) {
                RequestId = this.RequestId, Owner = page.Owner, OwnerEpoch = page.OwnerEpoch,
                SnapshotId = page.SnapshotId, CapturedAt = page.CapturedAt,
            };
        }

        private void Fail(FriendListStatus status) { this.finished = true; this.players.Clear(); this.Failure = status; }
    }
}
