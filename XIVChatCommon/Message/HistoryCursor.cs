using MessagePack;

namespace XIVChatCommon.Message {
    [MessagePackObject]
    public sealed class HistoryCursor {
        [Key(0)] public string ServiceId { get; set; } = "";
        [Key(1)] public string RunId { get; set; } = "";
        [Key(2)] public long Sequence { get; set; }
    }
}
