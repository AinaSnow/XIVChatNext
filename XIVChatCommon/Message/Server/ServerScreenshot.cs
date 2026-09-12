using System;
using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public sealed class ServerScreenshot : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public string OwnerKey { get; set; } = "";
        [Key(2)] public string OwnerEpoch { get; set; } = "";
        [Key(3)] public ScreenshotStatus Status { get; set; }
        [Key(4)] public long CapturedAtUnixMilliseconds { get; set; }
        [Key(5)] public int Width { get; set; }
        [Key(6)] public int Height { get; set; }
        [Key(7)] public int TotalBytes { get; set; }
        [Key(8)] public int ChunkCount { get; set; }
        [Key(9)] public int ChunkIndex { get; set; }
        [Key(10)] public byte[] Data { get; set; } = Array.Empty<byte>();
        [Key(11)] public string Format { get; set; } = "image/jpeg";
        [Key(12)] public byte[] Digest { get; set; } = Array.Empty<byte>();
        [IgnoreMember] public bool Valid => ScreenshotProtocol.Valid(this);
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.Screenshot;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerScreenshot Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerScreenshot>(bytes);
    }
}
