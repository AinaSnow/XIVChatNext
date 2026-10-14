using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sodium;

namespace XIVChatCommon {
    /// <summary>
    /// Protocol 2 handshake. Each side contributes a fresh X25519 key, so every connection gets new session keys
    /// (recorded traffic cannot be replayed into a later connection, and past sessions stay private if a long-term key leaks).
    /// Each side proves possession of its long-term key with a key-confirmation tag. See docs/protocol/CHAT_V2.md.
    /// </summary>
    public static class KeyExchangeV2 {
        /// <summary>Sent by protocol 1 clients before the handshake.</summary>
        public static readonly byte[] LegacyMagic = { 14, 20, 67 };
        /// <summary>Sent by protocol 2 clients. Protocol 1 plugins reject it by closing the connection.</summary>
        public static readonly byte[] Magic = { 14, 20, 68 };
        private const int KeySize = 32, TagSize = 32;
        private static readonly byte[] Label = Encoding.ASCII.GetBytes("XIVChat Next protocol 2");

        public sealed record Result(byte[] RemotePublicKey, ISecureChannel Channel);

        /// <summary>The peer closed the connection before replying. A protocol 1 plugin does this when it sees <see cref="Magic"/>.</summary>
        public sealed class NotSupportedByPeerException : IOException {
            public NotSupportedByPeerException() : base("The peer closed the connection before answering the protocol 2 handshake.") { }
        }

        /// <summary>Runs after the client has written <see cref="Magic"/>.</summary>
        public static async Task<Result> ClientHandshake(KeyPair client, Stream stream, CancellationToken token = default) {
            var ephemeral = PublicKeyBox.GenerateKeyPair();
            await stream.WriteAsync(Concat(ephemeral.PublicKey, client.PublicKey), token);
            await stream.FlushAsync(token);

            var reply = new byte[KeySize * 2 + TagSize];
            int read;
            try {
                read = await stream.ReadAtLeastAsync(reply, 1, throwOnEndOfStream: false, token);
            } catch (IOException) {
                throw new NotSupportedByPeerException();
            }
            if (read == 0) throw new NotSupportedByPeerException();
            await stream.ReadExactlyAsync(reply.AsMemory(read), token);

            var serverEphemeral = reply[..KeySize];
            var serverStatic = reply[KeySize..(KeySize * 2)];
            var serverTag = reply[(KeySize * 2)..];
            var keys = Derive(
                Dh(ephemeral.PrivateKey, serverEphemeral),
                Dh(ephemeral.PrivateKey, serverStatic),
                Dh(client.PrivateKey, serverEphemeral),
                ephemeral.PublicKey, client.PublicKey, serverEphemeral, serverStatic);
            if (!CryptographicOperations.FixedTimeEquals(serverTag, keys.ServerTag))
                throw new CryptographicException("The plugin could not prove ownership of its key.");

            await stream.WriteAsync(keys.ClientTag, token);
            await stream.FlushAsync(token);
            return new Result(serverStatic, new CounterSecureChannel(keys.ServerToClient, keys.ClientToServer));
        }

        /// <summary>Runs after the server has read <see cref="Magic"/>.</summary>
        public static async Task<Result> ServerHandshake(KeyPair server, Stream stream, CancellationToken token = default) {
            var hello = new byte[KeySize * 2];
            await stream.ReadExactlyAsync(hello, token);
            var clientEphemeral = hello[..KeySize];
            var clientStatic = hello[KeySize..];

            var ephemeral = PublicKeyBox.GenerateKeyPair();
            var keys = Derive(
                Dh(ephemeral.PrivateKey, clientEphemeral),
                Dh(server.PrivateKey, clientEphemeral),
                Dh(ephemeral.PrivateKey, clientStatic),
                clientEphemeral, clientStatic, ephemeral.PublicKey, server.PublicKey);
            await stream.WriteAsync(Concat(ephemeral.PublicKey, server.PublicKey, keys.ServerTag), token);
            await stream.FlushAsync(token);

            var clientTag = new byte[TagSize];
            await stream.ReadExactlyAsync(clientTag, token);
            if (!CryptographicOperations.FixedTimeEquals(clientTag, keys.ClientTag))
                throw new CryptographicException("The client could not prove ownership of its key.");
            return new Result(clientStatic, new CounterSecureChannel(keys.ClientToServer, keys.ServerToClient));
        }

        internal sealed record Keys(byte[] ClientToServer, byte[] ServerToClient, byte[] ClientTag, byte[] ServerTag);

        /// <param name="ee">Client ephemeral × server ephemeral: fresh per connection.</param>
        /// <param name="es">Client ephemeral × server static: only the real plugin can compute it.</param>
        /// <param name="se">Client static × server ephemeral: only the real client can compute it.</param>
        internal static Keys Derive(byte[] ee, byte[] es, byte[] se, byte[] clientEphemeral, byte[] clientStatic, byte[] serverEphemeral, byte[] serverStatic) {
            // Binding every public key (and the protocol label) stops a party from splicing values between sessions or versions.
            var transcript = GenericHash.Hash(Concat(Label, clientEphemeral, clientStatic, serverEphemeral, serverStatic), null, KeySize);
            var secret = Concat(ee, es, se);
            byte[] Expand(string purpose) => GenericHash.Hash(Concat(Encoding.ASCII.GetBytes(purpose), secret), transcript, KeySize);
            var clientConfirm = Expand("client confirm");
            var serverConfirm = Expand("server confirm");
            return new Keys(Expand("client to server"), Expand("server to client"),
                GenericHash.Hash(transcript, clientConfirm, TagSize), GenericHash.Hash(transcript, serverConfirm, TagSize));
        }

        private static byte[] Dh(byte[] privateKey, byte[] publicKey) {
            if (publicKey.Length != KeySize) throw new CryptographicException("Invalid public key length.");
            byte[] shared;
            try {
                shared = ScalarMult.Mult(privateKey, publicKey);
            } catch (Exception ex) when (ex is not CryptographicException) {
                throw new CryptographicException("Invalid public key.", ex);
            }
            // A low-order public key yields an all-zero secret that an attacker could predict.
            if (shared.All(b => b == 0)) throw new CryptographicException("Invalid public key.");
            return shared;
        }

        private static byte[] Concat(params byte[][] parts) {
            var result = new byte[parts.Sum(p => p.Length)];
            var offset = 0;
            foreach (var part in parts) { part.CopyTo(result, offset); offset += part.Length; }
            return result;
        }
    }
}
