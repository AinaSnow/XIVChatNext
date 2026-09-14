using System.Net.WebSockets;
using XIVChat.Relay;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;

internal static class RawRelayChecks {
    internal static async Task Run(string address, RelayStore store, CancellationToken token, Action<bool, string> check) {
        var device = store.AddDevice("route-fixture");
        var other = store.AddDevice("unrelated-route-fixture");
        using var certificate = RelayTls.CreateCertificate();
        using var host = RelayEndpoint.CreateSocket(device.Credential);
        await host.ConnectAsync(RelayProtocol.Endpoint(address, "v1/host", true), token);
        await RelayWire.SendAsync(host, new HostHello(RelayProtocol.Version, RelayTls.Fingerprint(certificate)), token);
        _ = await RelayWire.ReadAsync<ControlMessage>(host, token);
        // Pairing is also safe under competing requests, without consuming an invitation on a version error.
        var invitation = store.Invite(device.Id) with { Server = address };
        check(store.Pair(new PairRequest(invitation.Code, "bad version", RelayProtocol.Version + 1)) == null, "incompatible pairing version rejected without consuming invitation");
        async Task<PairResult?> Redeem() {
            try { return await RelayEndpoint.PairAsync(invitation, "racing client", token); }
            catch (IOException) { return null; }
        }
        var pairs = await Task.WhenAll(Redeem(), Redeem());
        check(pairs.Count(pair => pair != null) == 1, "concurrent invitation redemption grants exactly one client");
        var pair = pairs.Single(pair => pair != null)!;
        using var client = RelayEndpoint.CreateSocket(pair.Credential);
        await client.ConnectAsync(RelayProtocol.Endpoint(address, "v1/client", true), token);
        var offer = await RelayWire.ReadAsync<ControlMessage>(host, token);
        async Task<bool> Denied(string credential, string ticket) {
            using var socket = RelayEndpoint.CreateSocket(credential);
            socket.Options.SetRequestHeader("X-Relay-Ticket", ticket);
            try { await socket.ConnectAsync(RelayProtocol.Endpoint(address, "v1/host-data/" + offer.SessionId, true), token); return false; }
            catch (WebSocketException) { return true; }
        }
        check(await Denied(other.Credential, offer.Ticket!), "another registered game device cannot claim a session even with its ticket");
        check(await Denied(device.Credential, RelayStore.Secret()), "wrong per-session ticket cannot claim the data route");
        using var data = RelayEndpoint.CreateSocket(device.Credential);
        data.Options.SetRequestHeader("X-Relay-Ticket", offer.Ticket);
        await data.ConnectAsync(RelayProtocol.Endpoint(address, "v1/host-data/" + offer.SessionId, true), token);
        _ = await RelayWire.ReadAsync<ControlMessage>(data, token);
        _ = await RelayWire.ReadAsync<ControlMessage>(client, token);
        check(await Denied(device.Credential, offer.Ticket!), "a claimed session ticket cannot attach a second data connection");
        await client.SendAsync(new byte[RelayProtocol.MaxDataBytes].AsMemory(), WebSocketMessageType.Binary, false, token);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var closed = false;
            try { closed = (await data.ReceiveAsync(new byte[1].AsMemory(), deadline.Token)).MessageType == WebSocketMessageType.Close; }
            catch (WebSocketException) { closed = true; }
            check(closed, "oversized fragmented frame closes its route before the deadline");
        }
        var replacement = store.Invite(device.Id);
        _ = store.Invite(device.Id);
        check(store.Pair(new PairRequest(replacement.Code, "stale invitation")) == null, "generating a new invitation invalidates the previous invitation");
        // A rejected data route must leave the host's independent control connection usable.
        check((await RelayEndpoint.InviteAsync(address, device.Credential, RelayTls.Fingerprint(certificate), token)).Code.Length == 64,
            "malformed session data leaves the game device control connection usable");
    }
}
