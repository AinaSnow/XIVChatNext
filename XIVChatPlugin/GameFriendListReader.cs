using System;
using System.Linq;
using System.Threading;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using XIVChatCommon.Message;

namespace XIVChatPlugin {
    /// <summary>Game pointers never leave the framework thread. Only the typed completion vfunc is intercepted.</summary>
    internal sealed unsafe class GameFriendListReader : IFriendListReader {
        private readonly Plugin plugin;
        private delegate void EndRequestDelegate(nint proxy);
        private Hook<EndRequestDelegate>? endHook;
        private nint watchedProxy;
        private nint hookedAddress;
        private CharacterIdentity? owner;
        private string epoch = "";
        private string? armedEpoch;
        private bool awaitingEnd;
        private FriendReadResult? result;
        private bool disposed;

        internal GameFriendListReader(Plugin plugin) => this.plugin = plugin;

        public void SetContext(CharacterIdentity? owner, string epoch) {
            this.owner = owner; this.epoch = epoch; this.Cancel();
        }

        public FriendListStatus? BeginRefresh() {
            if (this.disposed || !this.plugin.Framework.IsInFrameworkUpdateThread) return FriendListStatus.Unavailable;
            if (!this.plugin.PlayerState.IsLoaded || this.owner?.Key == null) return FriendListStatus.NotLoggedIn;
            try {
                var proxy = InfoProxyFriendList.Instance();
                if (proxy == null || proxy->VirtualTable == null) return FriendListStatus.Unavailable;
                if (this.watchedProxy != (nint)proxy) {
                    this.awaitingEnd = false; this.armedEpoch = null; this.watchedProxy = (nint)proxy;
                }
                var address = (nint)proxy->VirtualTable->EndRequest;
                if (address == 0) return FriendListStatus.Unavailable;
                if (this.endHook == null || address != this.hookedAddress) {
                    this.endHook?.Dispose();
                    this.endHook = this.plugin.GameInteropProvider.HookFromAddress<EndRequestDelegate>(address, this.OnEndRequest);
                    this.hookedAddress = address;
                    this.endHook.Enable();
                }
                // After a timeout/login change, drain the old completion before starting another request.
                // Otherwise a late EndRequest could be mistaken for the new request's response.
                if (this.awaitingEnd) return FriendListStatus.Busy;
                this.armedEpoch = this.epoch; this.awaitingEnd = true;
                if (proxy->RequestData()) return null;
                if (Volatile.Read(ref this.result) != null) return null; // synchronous completion
                this.awaitingEnd = false; this.armedEpoch = null;
                return FriendListStatus.Busy;
            } catch (Exception ex) {
                this.awaitingEnd = false; this.armedEpoch = null;
                Plugin.Log.Warning(ex, "Friend list refresh could not start.");
                return FriendListStatus.Unavailable;
            }
        }

        private void OnEndRequest(nint proxy) {
            this.endHook!.Original(proxy);
            if (this.disposed || proxy != this.watchedProxy) return;
            var requestEpoch = this.armedEpoch;
            this.armedEpoch = null; this.awaitingEnd = false;
            if (requestEpoch == null || requestEpoch != this.epoch) return;
            // Never copy native data from an unexpected callback thread.
            if (!this.plugin.Framework.IsInFrameworkUpdateThread) {
                Interlocked.Exchange(ref this.result, new FriendReadResult(requestEpoch, Array.Empty<Player>(), FriendListStatus.Unavailable));
                return;
            }
            try {
                if (!this.plugin.PlayerState.IsLoaded || this.owner == null || this.plugin.PlayerState.ContentId != this.owner.ContentId) {
                    Interlocked.Exchange(ref this.result, new FriendReadResult(requestEpoch, Array.Empty<Player>(), FriendListStatus.IdentityChanged));
                    return;
                }
                var list = (InfoProxyFriendList*)proxy;
                var count = list->EntryCount;
                if (count > FriendListProtocol.MaxFriends || (count > 0 && list->CharData == null)) throw new InvalidOperationException("Invalid friend count or buffer.");
                var players = new Player[count];
                var worlds = this.plugin.DataManager.GetExcelSheet<World>();
                var jobs = this.plugin.DataManager.GetExcelSheet<ClassJob>();
                var territories = this.plugin.DataManager.GetExcelSheet<TerritoryType>();
                var entries = list->CharDataSpan;
                for (int i = 0; i < players.Length; i++) {
                    var entry = entries[i];
                    players[i] = new Player {
                        ContentId = entry.ContentId, Name = entry.NameString, FreeCompany = entry.FCTagString,
                        Status = (ulong)entry.State, CurrentWorld = entry.CurrentWorld, HomeWorld = entry.HomeWorld,
                        CurrentWorldName = worlds.GetRowOrDefault(entry.CurrentWorld)?.Name.ExtractText(),
                        HomeWorldName = worlds.GetRowOrDefault(entry.HomeWorld)?.Name.ExtractText(),
                        Job = entry.Job, JobName = jobs.GetRowOrDefault(entry.Job)?.Name.ExtractText(),
                        Territory = entry.Location, TerritoryName = territories.GetRowOrDefault(entry.Location)?.PlaceName.ValueNullable?.Name.ExtractText(),
                        GrandCompany = (byte)entry.GrandCompany, MainLanguage = (byte)entry.ClientLanguage, Languages = (byte)entry.Languages,
                        IdentityUnavailable = entry.HomeWorld == 0 || string.IsNullOrWhiteSpace(entry.NameString),
                    };
                }
                var status = FriendListProtocol.ValidPlayers(players) ? FriendListStatus.Success : FriendListStatus.Failed;
                if (status != FriendListStatus.Success) {
                    Plugin.Log.Warning("Friend snapshot rejected: count={Count}, zeroCid={ZeroCid}, zeroHomeWorld={ZeroHomeWorld}, invalidName={InvalidName}, duplicateCid={DuplicateCid}",
                        players.Length, players.Count(p => p.ContentId == 0), players.Count(p => p.HomeWorld == 0),
                        players.Count(p => string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 64),
                        players.Length - players.Select(p => p.ContentId).Distinct().Count());
                }
                Interlocked.Exchange(ref this.result, new FriendReadResult(requestEpoch, players, status));
            } catch (Exception ex) {
                Plugin.Log.Warning(ex, "Friend list completed but its snapshot could not be read.");
                Interlocked.Exchange(ref this.result, new FriendReadResult(requestEpoch, Array.Empty<Player>(), FriendListStatus.Failed));
            }
        }

        public FriendReadResult? TakeResult() => Interlocked.Exchange(ref this.result, null);
        public void Cancel() { this.armedEpoch = null; Interlocked.Exchange(ref this.result, null); }
        public void Dispose() { this.disposed = true; this.Cancel(); this.endHook?.Dispose(); }
    }
}
