using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sodium;
using XIVChatCommon.Message;

namespace XIVChatCommon {
    /// <summary>Encrypts and frames application packets after a handshake. One reader and one writer may use it concurrently.</summary>
    public interface ISecureChannel {
        /// <summary>1 for the original XIVChat protocol, 2 for the ephemeral-key protocol.</summary>
        int Protocol { get; }
        Task<byte[]> ReadAsync(Stream stream, CancellationToken token = default);
        Task WriteAsync(Stream stream, byte[] message, CancellationToken token = default);
    }

    public static class SecureChannelExtensions {
        public static Task WriteAsync(this ISecureChannel channel, Stream stream, Encodable message, CancellationToken token = default) =>
            channel.WriteAsync(stream, message.Encode(), token);
    }

    /// <summary>Original framing: random nonce per packet and fixed session keys. Kept for older peers.</summary>
    public sealed class LegacySecureChannel : ISecureChannel {
        private readonly SessionKeys keys;
        public LegacySecureChannel(SessionKeys keys) => this.keys = keys;
        public int Protocol => 1;
        public Task<byte[]> ReadAsync(Stream stream, CancellationToken token = default) => SecretMessage.ReadSecretMessage(stream, this.keys.rx, token);
        public Task WriteAsync(Stream stream, byte[] message, CancellationToken token = default) => SecretMessage.SendSecretMessage(stream, this.keys.tx, message, token);
    }

    /// <summary>
    /// Protocol 2 framing: a 4-byte little-endian ciphertext length, then XSalsa20-Poly1305 ciphertext.
    /// The nonce is an implicit per-direction counter, so a replayed, reordered, dropped or forged packet fails to open.
    /// </summary>
    public sealed class CounterSecureChannel : ISecureChannel {
        public const uint MaxCiphertextLength = 128_000;
        private readonly byte[] receiveKey, sendKey;
        private ulong received, sent;

        public CounterSecureChannel(byte[] receiveKey, byte[] sendKey) {
            if (receiveKey.Length != 32 || sendKey.Length != 32) throw new ArgumentException("Session keys must be 32 bytes.");
            this.receiveKey = receiveKey;
            this.sendKey = sendKey;
        }

        public int Protocol => 2;

        public static byte[] Nonce(ulong counter) {
            var nonce = new byte[SecretMessage.NonceSize];
            BinaryPrimitives.WriteUInt64LittleEndian(nonce, counter);
            return nonce;
        }

        public async Task<byte[]> ReadAsync(Stream stream, CancellationToken token = default) {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, token);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length < SecretMessage.MacSize || length > MaxCiphertextLength)
                throw new InvalidDataException($"Encrypted packet length {length} is outside {SecretMessage.MacSize}–{MaxCiphertextLength}.");
            var ciphertext = new byte[length];
            await stream.ReadExactlyAsync(ciphertext, token);
            if (this.received == ulong.MaxValue) throw new InvalidDataException("Receive counter exhausted.");
            // Throws CryptographicException for anything not sealed with the next expected counter.
            var plaintext = SecretBox.Open(ciphertext, Nonce(this.received), this.receiveKey);
            this.received++;
            return plaintext;
        }

        public async Task WriteAsync(Stream stream, byte[] message, CancellationToken token = default) {
            if (message.Length + SecretMessage.MacSize > MaxCiphertextLength) throw new ArgumentOutOfRangeException(nameof(message), "Packet is too large.");
            if (this.sent == ulong.MaxValue) throw new InvalidOperationException("Send counter exhausted.");
            var ciphertext = SecretBox.Create(message, Nonce(this.sent++), this.sendKey);
            var frame = new byte[4 + ciphertext.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)ciphertext.Length);
            ciphertext.CopyTo(frame, 4);
            // One write per frame so a cancelled write cannot leave a half header followed by another packet.
            await stream.WriteAsync(frame, token);
            await stream.FlushAsync(token);
        }
    }
}
