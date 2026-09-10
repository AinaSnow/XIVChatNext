using System;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using XIVChatCommon.Message;

namespace XIVChatPlugin {
    internal sealed unsafe class GameFriendPresenceReader : IFriendPresenceReader {
        private readonly Plugin plugin;
        // InfoModule dispatches (proxy, response, CID) to slot 9 before slot 8.
        // See docs/friend-presence.md for the verified ABI and result semantics.
        private delegate void ResponseDelegate(nint proxy, nint response, ulong contentId);
        private Hook<ResponseDelegate>? hook;
        private nint watchedProxy, address;
        private CharacterIdentity? owner;
        private string epoch = "";
        private string? armedEpoch;
        private ulong awaitingCid;
        private readonly HashSet<ulong> draining = new();
        private PresenceReadResult? result;
        private bool disposed;
        internal GameFriendPresenceReader(Plugin plugin) => this.plugin = plugin;
        public void SetContext(CharacterIdentity? owner, string epoch) { this.owner = owner; this.epoch = epoch; this.Cancel(); }
        public FriendListStatus? Begin(ulong contentId) {
            if (this.disposed || !this.plugin.Framework.IsInFrameworkUpdateThread) return FriendListStatus.Unavailable;
            if (!this.plugin.PlayerState.IsLoaded || this.owner?.ContentId != this.plugin.PlayerState.ContentId) return FriendListStatus.NotLoggedIn;
            try {
                var proxy = InfoProxyFriendList.Instance();
                var agent = AgentFriendlist.Instance();
                if (proxy == null || proxy->VirtualTable == null || agent == null || proxy->EntryCount > FriendListProtocol.MaxFriends || proxy->CharData == null) return FriendListStatus.Unavailable;
                bool found = false;
                foreach (var entry in proxy->CharDataSpan) if (entry.ContentId == contentId) { found = true; break; }
                if (!found) return FriendListStatus.Unavailable;
                if (this.watchedProxy != (nint)proxy) { this.awaitingCid = 0; this.draining.Clear(); this.watchedProxy = (nint)proxy; }
                if (this.awaitingCid != 0 || this.draining.Contains(contentId) || this.draining.Count >= FriendListProtocol.MaxFriends) return FriendListStatus.Busy;
                var callback = ((nint*)proxy->VirtualTable)[9];
                // Scan Dalamud's original module copy: live code may already contain a hook jump after reload.
                if (this.hook == null || this.address != callback) {
                    var verified = this.plugin.ScanText("4D 85 C0 0F 84 ?? ?? ?? ?? 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 44 8B 51 10 45 33 C9 49 8B F0 48 8B FA 48 8B E9");
                    if (callback == 0 || callback != verified) return FriendListStatus.Unavailable;
                    this.hook?.Dispose(); this.address = callback;
                    this.hook = this.plugin.GameInteropProvider.HookFromAddress<ResponseDelegate>(callback, this.OnResponse);
                    this.hook.Enable();
                }
                this.armedEpoch = this.epoch; this.awaitingCid = contentId;
                agent->RequestFriendInfo(contentId);
                return null;
            } catch (Exception ex) {
                this.Cancel(); Plugin.Log.Warning(ex, "Friend presence query could not start.");
                return FriendListStatus.Unavailable;
            }
        }
        private void OnResponse(nint proxy, nint response, ulong contentId) {
            this.hook!.Original(proxy, response, contentId);
            if (proxy == this.watchedProxy && this.draining.Remove(contentId)) return;
            if (this.disposed || proxy != this.watchedProxy || contentId == 0 || contentId != this.awaitingCid) return;
            var requestEpoch = this.armedEpoch;
            this.awaitingCid = 0; this.armedEpoch = null;
            if (requestEpoch == null || requestEpoch != this.epoch) return;
            var presence = PresenceState.Unknown;
            var status = FriendListStatus.Unavailable;
            try {
                if (this.plugin.Framework.IsInFrameworkUpdateThread && this.plugin.PlayerState.IsLoaded &&
                    this.owner?.ContentId == this.plugin.PlayerState.ContentId && response != 0 && *(ulong*)((byte*)response + 8) == contentId) {
                    var code = *(ushort*)((byte*)response + 0x14);
                    // 0 = found online; 1 = offline; 2 = unresolved/cross-DC; 3 = no usable update.
                    status = code <= 2 ? FriendListStatus.Success : FriendListStatus.Unavailable;
                    presence = code == 0 ? PresenceState.Online : code == 1 ? PresenceState.Offline : PresenceState.Unknown;
                    Plugin.Log.Debug("Friend presence response: result={Result}, presence={Presence}", code, presence);
                }
            } catch (Exception ex) { Plugin.Log.Warning(ex, "Friend presence response could not be read."); }
            Interlocked.Exchange(ref this.result, new PresenceReadResult(requestEpoch, contentId, presence, status));
        }
        public PresenceReadResult? TakeResult() => Interlocked.Exchange(ref this.result, null);
        // Quarantine only the canceled CID. Other friends can still be queried after a lost response.
        public void Cancel() { if (this.awaitingCid != 0) this.draining.Add(this.awaitingCid); this.awaitingCid = 0; this.armedEpoch = null; Interlocked.Exchange(ref this.result, null); }
        public void Dispose() { this.disposed = true; this.Cancel(); this.hook?.Dispose(); }
    }
}
