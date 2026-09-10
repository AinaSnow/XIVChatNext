using XIVChatCommon.Message;
using XIVChatPlugin;
using XIVChat_Desktop;

internal static class PresenceTests {
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static readonly CharacterIdentity Owner = new() { ContentId = 10, Name = "Owner", HomeWorldId = 1 };
    private static ClientFriendPresence Request(ulong cid = 20, string epoch = "login") => new() { RequestId = Guid.NewGuid().ToString("N"), OwnerKey = Owner.Key!, OwnerEpoch = epoch, ContentId = cid };
    private static ServerFriendPresence Reply(ClientFriendPresence request, DateTime now) => new() {
        RequestId = request.RequestId, OwnerKey = request.OwnerKey, OwnerEpoch = request.OwnerEpoch, ContentId = request.ContentId,
        Presence = PresenceState.Online, CheckedAt = now, Status = FriendListStatus.Success,
    };
    public static Task Session() {
        var now = DateTime.UtcNow;
        var state = new FriendPresenceSession();
        state.SetContext("a", Owner.Key, "login", false);
        Check(state.Begin(20, now) == null, "Legacy server queried");
        state.SetContext("a", Owner.Key, "login", true);
        var request = state.Begin(20, now)!;
        Check(request.Valid && ClientFriendPresence.Decode(request.Encode()[1..]).ContentId == 20, "Request wire changed");
        Check(!new ClientFriendPresence { RequestId = null! }.Valid, "Null request accepted");
        Check(state.Begin(21, now) == null, "Concurrent native requests allowed");
        var response = Reply(request, now);
        response.ContentId = 21;
        Check(!state.Add(response, now), "Wrong friend response accepted");
        response.ContentId = 20; response.OwnerEpoch = "old";
        Check(!state.Add(response, now), "Old login response accepted");
        response.OwnerEpoch = "login"; response.Presence = (PresenceState)99;
        Check(!state.Add(response, now), "Invalid presence enum accepted");
        response.Presence = PresenceState.Online; response.CheckedAt = now.AddMinutes(5);
        Check(!state.Add(response, now), "Future freshness accepted");
        response.CheckedAt = now;
        Check(state.Add(ServerFriendPresence.Decode(response.Encode()[1..]), now), "Valid response rejected");
        Check(FriendPresenceSession.Fresh(state.Get(20), now) && !FriendPresenceSession.Fresh(state.Get(20), now.AddMinutes(1)), "Expiry boundary incorrect");
        Check(state.Begin(20, now.AddSeconds(29)) == null, "Per-friend rate limit bypassed");
        var timed = state.Begin(21, now)!;
        state.Expire(now.AddSeconds(15));
        Check(state.Get(21)?.Status == FriendListStatus.TimedOut && !state.Add(Reply(timed, now), now.AddSeconds(16)), "Late response escaped timeout");
        var old = state.Begin(22, now.AddSeconds(16))!;
        state.SetContext("b", Owner.Key, "login", true);
        Check(state.Get(20) == null && !state.Add(Reply(old, now), now), "Source switch retained presence");
        state.Begin(20, now);
        state.SetContext("b", Owner.Key, "new-login", true);
        Check(!state.IsPending(20) && state.Get(20) == null, "Login switch retained request");
        state.SetContext("b", null, null, false);
        Check(state.Begin(20, now) == null, "Disconnected status query escaped");
        return Task.CompletedTask;
    }
    private sealed class Reader : IFriendPresenceReader {
        internal int Calls, Cancels;
        internal ulong Cid;
        internal PresenceReadResult? Result;
        public void SetContext(CharacterIdentity? owner, string epoch) => this.Cancel();
        public FriendListStatus? Begin(ulong cid) { this.Calls++; this.Cid = cid; return null; }
        public PresenceReadResult? TakeResult() { var result = this.Result; this.Result = null; return result; }
        public void Cancel() => this.Cancels++;
        public void Dispose() { }
    }
    public static Task Coordinator() {
        var now = DateTime.UtcNow;
        var reader = new Reader();
        var responses = new List<ServerFriendPresence>();
        using var coordinator = new FriendPresenceCoordinator(reader, (_, value) => responses.Add(value));
        var first = new PresenceRequest(Guid.NewGuid(), Request(), CancellationToken.None);
        var second = new PresenceRequest(Guid.NewGuid(), Request(), CancellationToken.None);
        coordinator.Enqueue(first); coordinator.Enqueue(second); coordinator.Tick(Owner, "login", now);
        Check(reader.Calls == 1 && reader.Cid == 20 && responses.Count == 0, "Same friend requests not coalesced");
        reader.Result = new("old", 20, PresenceState.Online, FriendListStatus.Success);
        coordinator.Tick(Owner, "login", now.AddMilliseconds(100));
        Check(responses.Count == 0, "Old native response published");
        reader.Result = new("login", 20, PresenceState.Online, FriendListStatus.Success);
        coordinator.Tick(Owner, "login", now.AddSeconds(1));
        Check(responses.Count == 2 && responses.All(x => x.Presence == PresenceState.Online), "Coalesced replies missing");
        coordinator.Enqueue(new(Guid.NewGuid(), Request(), CancellationToken.None));
        coordinator.Tick(Owner, "login", now.AddSeconds(2));
        Check(reader.Calls == 1 && responses[^1].CheckedAt == now.AddSeconds(1), "Cache request changed freshness or queried again");
        coordinator.Enqueue(new(Guid.NewGuid(), Request(21), CancellationToken.None));
        coordinator.Tick(Owner, "login", now.AddSeconds(5));
        Check(reader.Calls == 2, "Different friend query not started");
        coordinator.Tick(Owner, "login", now.AddSeconds(15));
        Check(responses[^1].Status == FriendListStatus.TimedOut, "Native timeout missing");
        reader.Result = new("login", 21, PresenceState.Online, FriendListStatus.Success);
        var count = responses.Count;
        coordinator.Tick(Owner, "login", now.AddSeconds(16));
        Check(responses.Count == count, "Late native reply published");
        coordinator.Enqueue(new(Guid.NewGuid(), Request(22), CancellationToken.None));
        coordinator.Tick(Owner, "login", now.AddSeconds(17));
        coordinator.Tick(Owner, "next", now.AddSeconds(18));
        Check(responses[^1].Status == FriendListStatus.IdentityChanged, "Login change did not terminate request");
        coordinator.Enqueue(new(Guid.NewGuid(), Request(22), CancellationToken.None));
        coordinator.Tick(Owner, "next", now.AddSeconds(19));
        Check(responses[^1].Status == FriendListStatus.IdentityChanged, "Old queued owner accepted");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        count = responses.Count;
        coordinator.Enqueue(new(Guid.NewGuid(), Request(22, "next"), canceled.Token));
        coordinator.Tick(Owner, "next", now.AddSeconds(20));
        Check(responses.Count == count, "Disconnected peer got a reply");
        using var bounded = new FriendPresenceCoordinator(new Reader(), (_, _) => { });
        for (int i = 0; i < 256; i++) Check(bounded.Enqueue(new(Guid.NewGuid(), Request(), CancellationToken.None)), "Queue prematurely full");
        Check(!bounded.Enqueue(new(Guid.NewGuid(), Request(), CancellationToken.None)), "Queue unbounded");
        return Task.CompletedTask;
    }
}
