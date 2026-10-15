using System;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using XIVChatCommon;
using XIVChatCommon.Message;

namespace XIVChatPlugin {
    /// <summary>
    /// Current names of the character's linkshells and cross-world linkshells, read from the game's info proxies on the
    /// framework thread. Chat lines only carry the slot number, so the name is attached when a message is recorded.
    /// </summary>
    internal sealed unsafe class GameLinkshellNames {
        private const int Slots = 8;
        private readonly string?[] linkshells = new string?[Slots];
        private readonly string?[] crossWorld = new string?[Slots];
        private long nextRefresh;

        /// <summary>Call from the framework thread. Clears names while logged out.</summary>
        internal void Refresh(bool loggedIn, Action<Exception>? reportFailure = null) {
            if (!loggedIn) { Array.Clear(this.linkshells); Array.Clear(this.crossWorld); this.nextRefresh = 0; return; }
            if (Environment.TickCount64 < this.nextRefresh) return;
            this.nextRefresh = Environment.TickCount64 + 5_000;
            try {
                var cross = InfoProxyCrossWorldLinkshell.Instance();
                if (cross != null)
                    for (var i = 0; i < Slots; i++) this.crossWorld[i] = Clean(cross->CrossWorldLinkshells[i].Name.ToString());
                var local = InfoProxyLinkshell.Instance();
                if (local != null)
                    for (var i = 0; i < Slots; i++) {
                        var info = local->GetLinkshellInfo((uint)i);
                        this.linkshells[i] = info == null || info->Id == 0 ? null : Clean(local->GetLinkshellName(info->Id).ToString());
                    }
            } catch (Exception ex) {
                // Names are optional; chat keeps working with slot numbers only.
                reportFailure?.Invoke(ex);
                this.nextRefresh = Environment.TickCount64 + 60_000;
            }
        }

        /// <summary>The linkshell name for a linkshell or cross-world linkshell message, if known.</summary>
        internal string? NameFor(ChatType type) => LinkshellSlots.Of(type) is { } slot
            ? (slot.CrossWorld ? this.crossWorld : this.linkshells)[slot.Index]
            : null;

        private static string? Clean(string? name) {
            name = name?.Trim();
            return string.IsNullOrEmpty(name) ? null : name.Length > 64 ? name[..64] : name;
        }
    }
}
