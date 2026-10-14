using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Sodium;
using XIVChatCommon;

internal static class Program {
    private static int checks;
    private static void Check(bool value, string name) {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name); checks++;
    }
    private static async Task<bool> Fails(Func<Task> action) {
        try { await action(); return false; } catch (Exception) { return true; }
    }

    /// <summary>A connected loopback TCP pair; nothing leaves this computer.</summary>
    private static async Task<(NetworkStream Client, NetworkStream Server, IDisposable Cleanup)> Pair() {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var client = new TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var server = await accept; listener.Stop();
        return (client.GetStream(), server.GetStream(), new Cleanup(client, server));
    }
    private sealed class Cleanup(TcpClient a, TcpClient b) : IDisposable { public void Dispose() { a.Dispose(); b.Dispose(); } }

    private static async Task<(KeyExchangeV2.Result Client, KeyExchangeV2.Result Server, NetworkStream ClientStream, NetworkStream ServerStream, IDisposable Cleanup)>
        Handshake(KeyPair client, KeyPair server) {
        var (c, s, cleanup) = await Pair();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = KeyExchangeV2.ServerHandshake(server, s, timeout.Token);
        var clientResult = await KeyExchangeV2.ClientHandshake(client, c, timeout.Token);
        return (clientResult, await serverTask, c, s, cleanup);
    }

    private static byte[] Frame(ISecureChannel channel, byte[] message) {
        using var buffer = new MemoryStream();
        channel.WriteAsync(buffer, message).GetAwaiter().GetResult();
        return buffer.ToArray();
    }

    public static async Task<int> Main() {
        try {
            var clientKey = PublicKeyBox.GenerateKeyPair();
            var serverKey = PublicKeyBox.GenerateKeyPair();

            var first = await Handshake(clientKey, serverKey);
            Check(first.Client.RemotePublicKey.SequenceEqual(serverKey.PublicKey) && first.Server.RemotePublicKey.SequenceEqual(clientKey.PublicKey),
                "Each side learns the other's long-term public key");
            Check(first.Client.Channel.Protocol == 2 && first.Server.Channel.Protocol == 2, "Protocol 2 channels are negotiated");
            for (var i = 0; i < 3; i++) await first.Client.Channel.WriteAsync(first.ClientStream, Encoding.UTF8.GetBytes("hello " + i));
            var echoes = new List<string>();
            for (var i = 0; i < 3; i++) echoes.Add(Encoding.UTF8.GetString(await first.Server.Channel.ReadAsync(first.ServerStream)));
            await first.Server.Channel.WriteAsync(first.ServerStream, Encoding.UTF8.GetBytes("reply"));
            Check(echoes.SequenceEqual(["hello 0", "hello 1", "hello 2"]) && Encoding.UTF8.GetString(await first.Client.Channel.ReadAsync(first.ClientStream)) == "reply",
                "Packets flow in both directions in order");

            // Recorded traffic from one connection must not be accepted by a later connection between the same devices.
            var recorded = Frame(first.Client.Channel, Encoding.UTF8.GetBytes("/tell Someone recorded"));
            var second = await Handshake(clientKey, serverKey);
            Check(await Fails(() => second.Server.Channel.ReadAsync(new MemoryStream(recorded))), "A packet recorded from an earlier connection is rejected");
            first.Cleanup.Dispose();

            // Within a connection, the implicit counter rejects replay, reordering and tampering.
            var key = RandomNumberGenerator.GetBytes(32);
            var sender = new CounterSecureChannel(new byte[32], key);
            var a = Frame(sender, [1]); var b = Frame(sender, [2]);
            var receiver = new CounterSecureChannel(key, new byte[32]);
            Check((await receiver.ReadAsync(new MemoryStream(a)))[0] == 1 && await Fails(() => receiver.ReadAsync(new MemoryStream(a))), "Replaying a packet within a connection is rejected");
            Check(await Fails(() => new CounterSecureChannel(key, new byte[32]).ReadAsync(new MemoryStream(b))), "A packet delivered out of order is rejected");
            var tampered = (byte[])a.Clone(); tampered[^1] ^= 1;
            Check(await Fails(() => new CounterSecureChannel(key, new byte[32]).ReadAsync(new MemoryStream(tampered))), "A modified packet is rejected");
            var oversized = new byte[4]; BitConverter.GetBytes(CounterSecureChannel.MaxCiphertextLength + 1).CopyTo(oversized, 0);
            Check(await Fails(() => receiver.ReadAsync(new MemoryStream(oversized))), "An oversized frame length is rejected before reading");

            // Knowing a trusted client's public key is not enough to connect as that client.
            var impostor = new KeyPair(clientKey.PublicKey, PublicKeyBox.GenerateKeyPair().PrivateKey);
            {
                var (c, s, cleanup) = await Pair();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var server = KeyExchangeV2.ServerHandshake(serverKey, s, timeout.Token);
                var clientFailed = await Fails(() => KeyExchangeV2.ClientHandshake(impostor, c, timeout.Token));
                c.Close();
                Check(clientFailed && await Fails(() => server), "A client without the matching private key cannot complete the handshake");
                cleanup.Dispose();
            }
            // Likewise a plugin cannot pose as a trusted plugin.
            var fakeServer = new KeyPair(serverKey.PublicKey, PublicKeyBox.GenerateKeyPair().PrivateKey);
            {
                var (c, s, cleanup) = await Pair();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var server = KeyExchangeV2.ServerHandshake(fakeServer, s, timeout.Token);
                var error = await Record(() => KeyExchangeV2.ClientHandshake(clientKey, c, timeout.Token));
                Check(error is CryptographicException, "The client rejects a plugin that cannot prove its key");
                s.Close(); _ = await Record(() => server); cleanup.Dispose();
            }
            // A low-order ephemeral key would give a predictable shared secret.
            {
                var (c, s, cleanup) = await Pair();
                await c.WriteAsync(new byte[32].Concat(clientKey.PublicKey).ToArray());
                Check(await Record(() => KeyExchangeV2.ServerHandshake(serverKey, s)) is CryptographicException, "An all-zero ephemeral key is refused");
                cleanup.Dispose();
            }
            // A protocol 1 plugin closes the connection when it sees the new magic.
            {
                var (c, s, cleanup) = await Pair();
                var oldPlugin = Task.Run(async () => {
                    var magic = new byte[3]; await s.ReadExactlyAsync(magic);
                    if (!magic.SequenceEqual(KeyExchangeV2.LegacyMagic)) s.Close();
                });
                await c.WriteAsync(KeyExchangeV2.Magic);
                Check(await Record(() => KeyExchangeV2.ClientHandshake(clientKey, c)) is KeyExchangeV2.NotSupportedByPeerException,
                    "A protocol 1 plugin is detected so the client can decide whether to fall back");
                await oldPlugin; cleanup.Dispose();
            }
            Check(!KeyExchangeV2.Magic.SequenceEqual(KeyExchangeV2.LegacyMagic) && KeyExchangeV2.Magic.Length == KeyExchangeV2.LegacyMagic.Length,
                "Protocol 2 has its own magic of the same length");
            second.Cleanup.Dispose();

            Console.WriteLine($"All {checks} protocol checks passed.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static async Task<Exception?> Record(Func<Task> action) {
        try { await action(); return null; } catch (Exception ex) { return ex; }
    }
}
