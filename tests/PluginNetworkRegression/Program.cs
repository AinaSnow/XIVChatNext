using System.Net;
using System.Net.Sockets;
using Sodium;
using XIVChatCommon;
using XIVChatPlugin;

internal static class Program {
    private static int checks;
    private static void Check(bool value, string name) {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name); checks++;
    }
    /// <summary>Runs the plugin's negotiation against a client over loopback TCP.</summary>
    private static async Task<(ProtocolNegotiation.Outcome Plugin, Exception? ClientError)> Negotiate(
        byte[] magic, KeyPair client, KeyPair plugin, bool allowLegacy, Func<byte[], bool> usedProtocol2) {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var clientSocket = new TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await clientSocket.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSocket = await accept; listener.Stop();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pluginSide = ProtocolNegotiation.AcceptAsync(serverSocket.GetStream(), plugin, allowLegacy, usedProtocol2, timeout.Token);
        Exception? clientError = null;
        try {
            var stream = clientSocket.GetStream();
            await stream.WriteAsync(magic, timeout.Token);
            if (magic.SequenceEqual(KeyExchangeV2.Magic)) await KeyExchangeV2.ClientHandshake(client, stream, timeout.Token);
            else if (magic.SequenceEqual(KeyExchangeV2.LegacyMagic)) await KeyExchange.ClientHandshake(client, stream, timeout.Token);
        } catch (Exception ex) { clientError = ex; }
        var outcome = await pluginSide;
        return (outcome, clientError);
    }

    public static async Task<int> Main() {
        try {
            long now = 0;
            var window = (long)ConnectionGate.TrustWindow.TotalMilliseconds;

            var gate = new ConnectionGate(() => now);
            for (var i = 0; i < ConnectionGate.MaxHandshakesPerSource; i++) gate.TryBeginHandshake("10.0.0.2");
            Check(!gate.TryBeginHandshake("10.0.0.2"), "A source cannot exceed the concurrent handshake limit");
            Check(gate.TryBeginHandshake("10.0.0.3"), "Another source is not affected by a busy source");
            gate.EndHandshake("10.0.0.2");
            Check(gate.TryBeginHandshake("10.0.0.2"), "Finishing a handshake frees a slot");
            for (var i = 0; i < ConnectionGate.MaxHandshakesPerSource; i++) gate.EndHandshake("10.0.0.2");
            gate.EndHandshake("10.0.0.3");
            gate.EndHandshake("10.0.0.3");
            Check(gate.Usage.Handshakes == 0, "Extra handshake releases do not underflow or leak entries");

            gate = new ConnectionGate(() => now);
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "A first trust request is allowed");
            Check(!gate.TryBeginTrustRequest("10.0.0.2"), "Only one trust prompt per source can be pending");
            Check(gate.TryBeginTrustRequest("10.0.0.3"), "Pending prompts from other sources are independent");
            gate.EndTrustRequest("10.0.0.2");
            now += 1000;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "A source may retry after its prompt closes");
            gate.EndTrustRequest("10.0.0.2");
            now += 1000;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "The third request within the window is allowed");
            gate.EndTrustRequest("10.0.0.2");
            Check(!gate.TryBeginTrustRequest("10.0.0.2"), "A fourth request within one minute is refused");
            now += window;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "Requests are allowed again after the window passes");
            gate.EndTrustRequest("10.0.0.2");
            gate.EndTrustRequest("10.0.0.3");

            gate = new ConnectionGate(() => now);
            for (var i = 0; i < 300; i++) { gate.TryBeginTrustRequest("192.168.0." + i); gate.EndTrustRequest("192.168.0." + i); }
            now += window;
            Check(gate.TryBeginTrustRequest("192.168.0.1"), "Many short-lived sources do not block a later request");
            Check(gate.Usage.PendingTrust == 1, "Pending trust count reflects open prompts only");

            var pluginKey = PublicKeyBox.GenerateKeyPair();
            var clientKey = PublicKeyBox.GenerateKeyPair();
            bool Never(byte[] _) => false;
            bool Always(byte[] _) => true;
            var modern = await Negotiate(KeyExchangeV2.Magic, clientKey, pluginKey, false, Never);
            Check(modern.Plugin.Accepted && modern.Plugin.Channel!.Protocol == 2 && modern.Plugin.RemoteKey!.SequenceEqual(clientKey.PublicKey),
                "The plugin accepts encryption v2 even when v1 is turned off");
            var legacy = await Negotiate(KeyExchangeV2.LegacyMagic, clientKey, pluginKey, true, Never);
            Check(legacy.Plugin.Accepted && legacy.Plugin.Channel!.Protocol == 1, "The plugin accepts an older client while v1 is allowed");
            Check(!(await Negotiate(KeyExchangeV2.LegacyMagic, clientKey, pluginKey, false, Never)).Plugin.Accepted, "The plugin refuses v1 when it is turned off");
            var downgrade = await Negotiate(KeyExchangeV2.LegacyMagic, clientKey, pluginKey, true, Always);
            Check(!downgrade.Plugin.Accepted && downgrade.Plugin.Refusal!.Contains("protocol 2"), "The plugin refuses v1 from a client that has used v2");
            Check(!(await Negotiate([1, 2, 3], clientKey, pluginKey, true, Never)).Plugin.Accepted, "The plugin refuses an unknown protocol magic");

            Console.WriteLine($"All {checks} plugin network checks passed.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine("FAIL " + ex);
            return 1;
        }
    }
}
