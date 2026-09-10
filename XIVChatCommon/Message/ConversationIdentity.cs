using System;
using System.Linq;
using MessagePack;

namespace XIVChatCommon.Message {
    // A conversation stays keyed by name + home world when a friend later supplies a ContentId.
    public static class ConversationIdentity {
        public static string? PeerKey(CharacterIdentity? peer) => peer?.IsComplete == true
            ? $"name:{peer.HomeWorldId}:{peer.Name.Trim().ToUpperInvariant()}" : null;
        public static bool IsTell(ushort channel) => (channel & 127) is (ushort)ChatType.TellIncoming or (ushort)ChatType.TellOutgoing;
        public static CharacterIdentity Copy(CharacterIdentity peer) => new() {
            Name = peer.Name, HomeWorldId = peer.HomeWorldId, HomeWorld = peer.HomeWorld, ContentId = peer.ContentId,
        };
    }

    [MessagePackObject]
    public sealed record TellTarget(
        [property: Key(0)] string Name = "", [property: Key(1)] ushort HomeWorldId = 0,
        [property: Key(2)] string HomeWorld = "", [property: Key(3)] ulong ContentId = 0) {
        [IgnoreMember] public bool IsValid => HomeWorldId > 0 && Name != null && HomeWorld != null && Name.Length is >= 3 and <= 64 &&
            Name.Split(' ').Length == 2 && Name.Split(' ').All(word => word.Length > 0 && word.All(c => char.IsLetter(c) || c is '\'' or '-')) &&
            HomeWorld.Length is >= 1 and <= 32 && HomeWorld.All(char.IsLetter);
        public static TellTarget From(CharacterIdentity identity) => new() {
            Name = identity.Name, HomeWorldId = identity.HomeWorldId, HomeWorld = identity.HomeWorld, ContentId = identity.ContentId,
        };
        public string Format(string body) {
            if (!IsValid || string.IsNullOrWhiteSpace(body) || body.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid Tell target or body.");
            return $"/tell {Name}@{HomeWorld} {body}";
        }
    }
}
