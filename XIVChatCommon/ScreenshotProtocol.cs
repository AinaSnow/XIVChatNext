using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using XIVChatCommon.Message.Server;

namespace XIVChatCommon {
    public enum ScreenshotQuality { Standard = 0, Detailed = 1 }
    public enum ScreenshotStatus { Capturing = 0, Data = 1, Busy = 2, NotLoggedIn = 3, Stale = 4, Unavailable = 5, TimedOut = 6, Cancelled = 7, TooLarge = 8 }

    public static class ScreenshotProtocol {
        public const int ChunkBytes = 48 * 1024;
        public const int MaxPacketBytes = 64 * 1024;
        public const int MaxImageBytes = 4 * 1024 * 1024;
        public const int MaxDimension = 2560;
        public const int MaxChunks = (MaxImageBytes + ChunkBytes - 1) / ChunkBytes;
        public const int CaptureTimeoutSeconds = 10;
        public const int TransferTimeoutSeconds = 30;
        public static int LongEdge(ScreenshotQuality quality) => quality == ScreenshotQuality.Detailed ? 2560 : 1920;
        public static float JpegQuality(ScreenshotQuality quality) => quality == ScreenshotQuality.Detailed ? 0.90f : 0.80f;
        public static bool ValidIdentity(string? request, string? owner, string? epoch) =>
            request?.Length is > 0 and <= 64 && owner?.Length is > 0 and <= 128 && epoch?.Length is > 0 and <= 64;
        public static bool Valid(ServerScreenshot reply) {
            if (!ValidIdentity(reply.RequestId, reply.OwnerKey, reply.OwnerEpoch) || !Enum.IsDefined(reply.Status) ||
                reply.Format != "image/jpeg" || reply.Data == null || reply.Digest == null) return false;
            if (reply.Status != ScreenshotStatus.Data)
                return reply.Data.Length == 0 && reply.Digest.Length == 0 && reply.TotalBytes == 0 && reply.ChunkCount == 0 &&
                    reply.ChunkIndex == 0 && reply.Width == 0 && reply.Height == 0 && reply.CapturedAtUnixMilliseconds == 0;
            return reply.Width is > 0 and <= MaxDimension && reply.Height is > 0 and <= MaxDimension &&
                reply.CapturedAtUnixMilliseconds is > 0 and <= 253402300799999 && reply.Digest.Length == 32 &&
                reply.TotalBytes is >= 4 and <= MaxImageBytes && reply.ChunkCount == (reply.TotalBytes + ChunkBytes - 1) / ChunkBytes &&
                reply.ChunkIndex >= 0 && reply.ChunkIndex < reply.ChunkCount &&
                reply.Data.Length == Math.Min(ChunkBytes, reply.TotalBytes - reply.ChunkIndex * ChunkBytes);
        }

        // Validate decoded dimensions before handing untrusted compressed data to a native image decoder.
        public static bool IsJpeg(ReadOnlySpan<byte> bytes, int expectedWidth, int expectedHeight) {
            if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) return false;
            int offset = 2;
            bool foundFrame = false;
            while (offset + 4 <= bytes.Length) {
                if (bytes[offset++] != 0xff) return false;
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) return false;
                byte marker = bytes[offset++];
                if (marker is 0xd9 or 0x00) return false;
                if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
                if (offset + 2 > bytes.Length) return false;
                int length = (bytes[offset] << 8) | bytes[offset + 1];
                if (length < 2 || length > bytes.Length - offset) return false;
                if (marker == 0xda) return foundFrame && length >= 6;
                if (marker is 0xc0 or 0xc1 or 0xc2) {
                    if (foundFrame || length < 8 || bytes[offset + 2] != 8) return false;
                    int height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                    int width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    if (width != expectedWidth || height != expectedHeight || width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension ||
                        bytes[offset + 7] is not (1 or 3) || length != 8 + 3 * bytes[offset + 7]) return false;
                    foundFrame = true;
                }
                offset += length;
            }
            return false;
        }
    }

    public sealed record ScreenshotImage(byte[] Bytes, int Width, int Height, long CapturedAtUnixMilliseconds);

    /// <summary>One bounded in-memory image; nothing is published until every authenticated chunk agrees.</summary>
    public sealed class ScreenshotAssembler {
        private readonly string request, owner, epoch;
        private ServerScreenshot? header;
        private byte[]? data;
        private bool[]? received;
        public int ReceivedBytes { get; private set; }
        public int TotalBytes => this.header?.TotalBytes ?? 0;
        public ScreenshotAssembler(string request, string owner, string epoch) {
            if (!ScreenshotProtocol.ValidIdentity(request, owner, epoch)) throw new ArgumentException("Invalid screenshot identity.");
            this.request = request; this.owner = owner; this.epoch = epoch;
        }
        public ScreenshotImage? Add(ServerScreenshot chunk) {
            if (!chunk.Valid || chunk.Status != ScreenshotStatus.Data || chunk.RequestId != this.request || chunk.OwnerKey != this.owner || chunk.OwnerEpoch != this.epoch)
                throw new InvalidDataException("Invalid screenshot chunk.");
            if (this.header == null) {
                this.header = chunk;
                this.data = new byte[chunk.TotalBytes];
                this.received = new bool[chunk.ChunkCount];
            }
            var h = this.header;
            if (h.TotalBytes != chunk.TotalBytes || h.Width != chunk.Width || h.Height != chunk.Height ||
                h.ChunkCount != chunk.ChunkCount || h.CapturedAtUnixMilliseconds != chunk.CapturedAtUnixMilliseconds || !h.Digest.SequenceEqual(chunk.Digest))
                throw new InvalidDataException("Screenshot metadata changed during transfer.");
            var destination = this.data!.AsSpan(chunk.ChunkIndex * ScreenshotProtocol.ChunkBytes, chunk.Data.Length);
            if (this.received![chunk.ChunkIndex]) {
                if (!destination.SequenceEqual(chunk.Data)) throw new InvalidDataException("Conflicting screenshot chunk.");
                return null;
            }
            chunk.Data.CopyTo(destination); this.received[chunk.ChunkIndex] = true; this.ReceivedBytes += chunk.Data.Length;
            if (this.ReceivedBytes != h.TotalBytes) return null;
            if (!SHA256.HashData(this.data!).SequenceEqual(h.Digest) || !ScreenshotProtocol.IsJpeg(this.data, h.Width, h.Height))
                throw new InvalidDataException("Invalid screenshot image.");
            return new ScreenshotImage(this.data!, h.Width, h.Height, h.CapturedAtUnixMilliseconds);
        }
    }
}
