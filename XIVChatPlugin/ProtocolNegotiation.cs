using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sodium;
using XIVChatCommon;

namespace XIVChatPlugin {
    /// <summary>Chooses protocol 1 or 2 from the client's magic and runs the handshake. Free of Dalamud so it can be tested directly.</summary>
    internal static class ProtocolNegotiation {
        internal sealed record Outcome(byte[]? RemoteKey, ISecureChannel? Channel, string? Refusal) {
            internal bool Accepted => this.Channel != null;
        }

        /// <param name="usedProtocol2">Whether this client key has completed protocol 2 before; such a key is refused over protocol 1.</param>
        internal static async Task<Outcome> AcceptAsync(Stream stream, KeyPair key, bool allowLegacy, Func<byte[], bool> usedProtocol2, CancellationToken token) {
            var magic = new byte[KeyExchangeV2.Magic.Length];
            await stream.ReadExactlyAsync(magic, token);
            if (magic.SequenceEqual(KeyExchangeV2.Magic)) {
                var secure = await KeyExchangeV2.ServerHandshake(key, stream, token);
                return new Outcome(secure.RemotePublicKey, secure.Channel, null);
            }
            if (!magic.SequenceEqual(KeyExchangeV2.LegacyMagic)) return new Outcome(null, null, "Unknown protocol.");
            if (!allowLegacy) return new Outcome(null, null, "Protocol 1 clients are turned off.");
            var legacy = await KeyExchange.ServerHandshake(key, stream, token);
            // Protocol 1 traffic for a key that has used protocol 2 is a downgrade or a replay of recorded packets.
            if (usedProtocol2(legacy.RemotePublicKey))
                return new Outcome(legacy.RemotePublicKey, null, "Refused a protocol 1 connection from a device that uses protocol 2.");
            return new Outcome(legacy.RemotePublicKey, new LegacySecureChannel(legacy.Keys), null);
        }
    }
}
