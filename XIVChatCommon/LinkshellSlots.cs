using XIVChatCommon.Message;

namespace XIVChatCommon {
    public static class LinkshellSlots {
        /// <summary>Zero-based slot of a linkshell or cross-world linkshell channel; null for any other channel.</summary>
        public static (bool CrossWorld, int Index)? Of(ChatType channel) {
            var value = (ushort)channel & 0x7F;
            if (value is >= (ushort)ChatType.Linkshell1 and <= (ushort)ChatType.Linkshell8) return (false, value - (ushort)ChatType.Linkshell1);
            if (value == (ushort)ChatType.CrossLinkshell1) return (true, 0);
            if (value is >= (ushort)ChatType.CrossLinkshell2 and <= (ushort)ChatType.CrossLinkshell8) return (true, value - (ushort)ChatType.CrossLinkshell2 + 1);
            return null;
        }
    }
}
