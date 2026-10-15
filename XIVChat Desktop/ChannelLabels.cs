using System;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    /// <summary>Readable channel names for exports, such as "Cross-world Linkshell [1] · Shell name".</summary>
    internal static class ChannelLabels {
        /// <param name="includeLinkshellName">False in streamer mode: a linkshell name can identify a community.</param>
        internal static string For(ServerMessage message, bool includeLinkshellName) {
            var type = (ChatType)((ushort)message.Channel & 0x7F);
            var key = "ChatType." + type;
            var label = Enum.IsDefined(type) ? LocalizationHelper.GetString(key) : "";
            if (string.IsNullOrEmpty(label) || label == key) label = Enum.IsDefined(type) ? type.ToString() : ((ushort)message.Channel).ToString();
            return includeLinkshellName && !string.IsNullOrWhiteSpace(message.ChannelName) ? label + " · " + message.ChannelName : label;
        }
    }
}
