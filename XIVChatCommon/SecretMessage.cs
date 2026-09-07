using Sodium;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon.Message;

namespace XIVChatCommon {
    public static class SecretMessage {
        private const uint MaxMessageLen = 128_000;

        public static async Task<byte[]> ReadSecretMessage(Stream s, byte[] key, CancellationToken token = default) {
            byte[] header = new byte[4 + NonceSize];
            await s.ReadExactlyAsync(header, 0, header.Length, token);

            var length = BitConverter.ToUInt32(header, 0);
            byte[] nonce = header.Skip(4).ToArray();

            if (length < MacSize || length > MaxMessageLen) {
                throw new ArgumentOutOfRangeException($"Encrypted message specified a size of {length}, outside the allowed range {MacSize}–{MaxMessageLen}");
            }

            byte[] ciphertext = new byte[length];
            await s.ReadExactlyAsync(ciphertext, 0, ciphertext.Length, token);

            return SecretBox.Open(ciphertext, nonce, key);
        }

        public static async Task SendSecretMessage(Stream s, byte[] key, byte[] message, CancellationToken token = default) {
            byte[] nonce = SecretBox.GenerateNonce();
            byte[] ciphertext = SecretBox.Create(message, nonce, key);
            byte[] len = BitConverter.GetBytes((uint)ciphertext.Length);

            if (ciphertext.Length > MaxMessageLen) {
                throw new ArgumentOutOfRangeException($"Encrypted message would be {len} bytes long, which is larger than the limit of {MaxMessageLen}");
            }

            await s.WriteAsync(len, 0, len.Length, token);
            await s.WriteAsync(nonce, 0, nonce.Length, token);
            await s.WriteAsync(ciphertext, 0, ciphertext.Length, token);
            await s.FlushAsync(token);
        }

        public static async Task SendSecretMessage(Stream s, byte[] key, Encodable message, CancellationToken token = default) {
            await SendSecretMessage(s, key, message.Encode(), token);
        }

        public const int MacSize = 16;
        public const int NonceSize = 24;
    }
}
