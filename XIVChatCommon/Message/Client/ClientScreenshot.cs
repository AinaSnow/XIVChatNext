using System;
using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public sealed class ClientScreenshot : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public string OwnerKey { get; set; } = "";
        [Key(2)] public string OwnerEpoch { get; set; } = "";
        [Key(3)] public ScreenshotQuality Quality { get; set; }
        [Key(4)] public bool Cancel { get; set; }
        [IgnoreMember] public bool Valid => ScreenshotProtocol.ValidIdentity(this.RequestId, this.OwnerKey, this.OwnerEpoch) && Enum.IsDefined(this.Quality);
        [IgnoreMember] protected override byte Code => (byte)ClientOperation.Screenshot;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ClientScreenshot Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ClientScreenshot>(bytes);
    }
}
