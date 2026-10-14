using System.Collections.Generic;
using XIVChatCommon;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    /// <summary>Groups backlog messages so each backlog packet stays under the encrypted frame limit.</summary>
    internal static class BacklogBatches {
        internal const int FrameLimit = 128_000;
        // Room for the packet header and encryption overhead around the messages.
        private const int Overhead = 5 + SecretMessage.NonceSize + SecretMessage.MacSize;

        /// <param name="messages">Messages with their encoded size, as recorded by <see cref="MessageBacklog"/>.</param>
        internal static IEnumerable<ServerMessage[]> Split(IEnumerable<(ServerMessage Message, int Bytes)> messages, int limit = FrameLimit) {
            var batch = new List<ServerMessage>();
            var size = Overhead;
            foreach (var (message, bytes) in messages) {
                if (batch.Count > 0 && size + bytes >= limit) {
                    yield return batch.ToArray();
                    batch.Clear();
                    size = Overhead;
                }
                size += bytes;
                batch.Add(message);
            }
            if (batch.Count > 0) yield return batch.ToArray();
        }
    }
}
