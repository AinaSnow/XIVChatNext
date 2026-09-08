using MessagePack;

namespace XIVChatCommon.Message {
    // Home world is part of identity; the current visiting world never is.
    [MessagePackObject]
    public sealed class CharacterIdentity {
        [Key(0)] public ulong ContentId { get; set; }
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public ushort HomeWorldId { get; set; }
        [Key(3)] public string HomeWorld { get; set; } = "";
        [IgnoreMember] public bool IsComplete => !string.IsNullOrWhiteSpace(this.Name) && this.HomeWorldId != 0;
        [IgnoreMember] public string? Key => this.ContentId != 0 ? $"cid:{this.ContentId}" :
            this.IsComplete ? $"name:{this.HomeWorldId}:{this.Name}" : null;
    }
}
