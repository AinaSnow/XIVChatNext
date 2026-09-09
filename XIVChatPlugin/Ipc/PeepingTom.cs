using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Lumina.Excel.Sheets;
using PeepingTom.Ipc;
using PeepingTom.Ipc.From;
using PeepingTom.Ipc.To;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin.Ipc {
    internal class PeepingTom : IDisposable {
        private Plugin Plugin { get; }
        private List<TargeterWithStatus> Targeters { get; } = [];
        private byte[] latest = new ServerPlayerList(PlayerListType.Targeting, Array.Empty<Player>()).Encode();
        private bool disposed;

        private class TargeterWithStatus {
            public Targeter Targeter { get; set; } = null!;
            public bool Targeting { get; set; }
        }

        internal PeepingTom(Plugin plugin) {
            this.Plugin = plugin;

            IpcInfo.GetSubscriber(this.Plugin.Interface).Subscribe(this.ReceiveMessage);
            IpcInfo.GetProvider(this.Plugin.Interface).SendMessage(new RequestTargetersMessage());
            this.Plugin.Events.NewClient += this.OnNewClient;
        }

        public void Dispose() {
            this.disposed = true;
            this.Plugin.Events.NewClient -= this.OnNewClient;
            IpcInfo.GetSubscriber(this.Plugin.Interface).Unsubscribe(this.ReceiveMessage);
        }

        private void ReceiveMessage(IFromMessage message) {
            if (this.disposed) return;
            if (!this.Plugin.Framework.IsInFrameworkUpdateThread) {
                _ = this.Plugin.Framework.RunOnFrameworkThread(() => this.ReceiveMessage(message));
                return;
            }
            switch (message) {
                case AllTargetersMessage allMessage: {
                    this.Targeters.Clear();
                    this.Targeters.AddRange(allMessage.Targeters.Take(200)
                        .Select(t => new TargeterWithStatus {
                            Targeter = t.targeter,
                            Targeting = t.currentlyTargeting,
                        }));
                    break;
                }
                case NewTargeterMessage newMessage: {
                    this.UpdateTargeter(newMessage.Targeter, true);

                    break;
                }
                case StoppedTargetingMessage stoppedMessage: {
                    this.UpdateTargeter(stoppedMessage.Targeter, false);

                    break;
                }
            }

            var xivChatMessage = this.GetMessage().Encode();
            Volatile.Write(ref this.latest, xivChatMessage);
            foreach (var client in this.Plugin.Server.Clients.Values) {
                this.SendToClient(client, xivChatMessage);
            }
        }

        private void UpdateTargeter(Targeter targeter, bool targeting) {
            var existing = this.Targeters.FirstOrDefault(t => t.Targeter.GameObjectId == targeter.GameObjectId);
            if (existing == default) {
                if (this.Targeters.Count >= 200) this.Targeters.RemoveAt(0);
                this.Targeters.Add(new TargeterWithStatus {
                    Targeter = targeter,
                    Targeting = targeting,
                });
            } else {
                existing.Targeter = targeter;
                existing.Targeting = targeting;
            }
        }

        private ServerPlayerList GetMessage() {
            var players = this.Targeters
                .Where(targeter => targeter.Targeting) // FIXME: send entire history so clients don't have to do logic
                .Select(targeter => this.Plugin.ObjectTable.FirstOrDefault(obj => obj.GameObjectId == targeter.Targeter.GameObjectId))
                .Where(actor => actor is IPlayerCharacter)
                .Cast<IPlayerCharacter>()
                .Select(chara => new Player {
                    Name = chara.Name.TextValue,
                    FreeCompany = chara.CompanyTag.TextValue,
                    Status = 0,
                    CurrentWorld = (ushort) chara.CurrentWorld.RowId,
                    CurrentWorldName = chara.CurrentWorld.Value.Name.ExtractText(),
                    HomeWorld = (ushort) chara.HomeWorld.RowId,
                    HomeWorldName = chara.HomeWorld.Value.Name.ExtractText(),
                    Territory = (ushort) this.Plugin.ClientState.TerritoryType,
                    TerritoryName = this.Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(this.Plugin.ClientState.TerritoryType)?.Name.ExtractText(),
                    Job = (byte) chara.ClassJob.RowId,
                    JobName = chara.ClassJob.Value.Name.ExtractText(),
                    GrandCompany = 0,
                    GrandCompanyName = null,
                    Languages = 0,
                    MainLanguage = 0,
                })
                .ToArray();
            return new ServerPlayerList(PlayerListType.Targeting, players);
        }

        private void SendToClient(BaseClient client, byte[] list) {
            if (!client.Ready || !client.GetPreference(ClientPreference.TargetingListSupport, false)) {
                return;
            }

            client.SendEncoded(list);
        }

        private void OnNewClient(Guid id, BaseClient client) {
            this.SendToClient(client, Volatile.Read(ref this.latest));
        }
    }
}
