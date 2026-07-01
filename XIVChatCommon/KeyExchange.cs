using Sodium;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace XIVChatCommon {
    public static class KeyExchange {
        public static SessionKeys ClientSessionKeys(KeyPair client, byte[] serverPublic) {
            var secret = ScalarMult.Mult(client.PrivateKey, serverPublic);

            var combined = secret
                .Concat(client.PublicKey)
                .Concat(serverPublic)
                .ToArray();

            var hash = GenericHash.Hash(combined, null, 64);

            byte[] rx = new byte[32];
            byte[] tx = new byte[32];

            for (var i = 0; i < 32; i++) {
                rx[i] = hash[i];
                tx[i] = hash[i + 32];
            }

            return new SessionKeys(rx, tx);
        }

        public static SessionKeys ServerSessionKeys(KeyPair server, byte[] clientPublic) {
            var secret = ScalarMult.Mult(server.PrivateKey, clientPublic);

            var combined = secret
                .Concat(clientPublic)
                .Concat(server.PublicKey)
                .ToArray();

            var hash = GenericHash.Hash(combined, null, 64);

            byte[] rx = new byte[32];
            byte[] tx = new byte[32];

            for (var i = 0; i < 32; i++) {
                tx[i] = hash[i];
                rx[i] = hash[i + 32];
            }

            return new SessionKeys(rx, tx);
        }

        public static async Task<HandshakeInfo> ServerHandshake(KeyPair server, Stream stream) {
            // get client public key
            byte[] clientPublic = new byte[32];
            await stream.ReadExactlyAsync(clientPublic, 0, clientPublic.Length);

            // send our public key
            await stream.WriteAsync(server.PublicKey, 0, server.PublicKey.Length);
            await stream.FlushAsync();

            // get shared secret and derive keys
            var keys = ServerSessionKeys(server, clientPublic);

            return new HandshakeInfo(clientPublic, keys);
        }

        public static async Task<HandshakeInfo> ClientHandshake(KeyPair client, Stream stream) {
            // send our public key
            await stream.WriteAsync(client.PublicKey, 0, client.PublicKey.Length);

            // get server public key
            byte[] serverPublic = new byte[32];
            await stream.ReadExactlyAsync(serverPublic, 0, serverPublic.Length);

            // get shared secret and derive keys
            var keys = ClientSessionKeys(client, serverPublic);

            return new HandshakeInfo(serverPublic, keys);
        }
    }

    public class SessionKeys {
        public readonly byte[] rx;
        public readonly byte[] tx;

        internal SessionKeys(byte[] rx, byte[] tx) {
            this.rx = rx;
            this.tx = tx;
        }
    }

    public class HandshakeInfo {
        public byte[] RemotePublicKey { get; }
        public SessionKeys Keys { get; }

        internal HandshakeInfo(byte[] remote, SessionKeys keys) {
            this.RemotePublicKey = remote;
            this.Keys = keys;
        }
    }
}
