using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVChatCommon.Message {
    /// <summary>Null means all channels (including future channel types); an empty array means none.</summary>
    public sealed class ChannelSubscription {
        private readonly HashSet<ushort>? channels;
        public static ChannelSubscription All { get; } = new(null);
        public ChannelSubscription(ushort[]? channels) {
            if (channels?.Length > 128 || channels?.Any(c => c > 127) == true)
                throw new ArgumentException("Invalid channel subscription.", nameof(channels));
            this.channels = channels == null ? null : new HashSet<ushort>(channels);
        }
        public bool Allows(ushort raw) => this.channels == null || this.channels.Contains((ushort)(raw & 127));
        public static ushort[]? Union(bool saveAllHistory, IEnumerable<ushort> visible, IEnumerable<ushort> notifications) =>
            saveAllHistory ? null : visible.Concat(notifications).Select(c => (ushort)(c & 127)).Distinct().OrderBy(c => c).ToArray();
    }
}
