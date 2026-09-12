using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    internal sealed class GameCardService : IDisposable {
        private readonly Plugin plugin;
        private readonly LinkMetadataCache metadata;
        private readonly GameCardCatalog catalog;
        private readonly Action<Guid, ServerGameCard> send;
        private readonly Action<ServerGameCard> broadcast;
        private readonly GameCardRequestQueue pending = new();
        private readonly List<GameCardRequest> waiting = new();
        private int equipmentDirty = 1;
        private long nextEquipmentRead;
        private long revision;
        private string equipmentSignature = "";
        private string equipmentScope = "";
        private EquipmentSnapshot? equipment;
        internal GameCardService(Plugin plugin, LinkMetadataCache metadata, Action<Guid, ServerGameCard> send, Action<ServerGameCard> broadcast) {
            this.plugin = plugin; this.metadata = metadata; this.catalog = new(plugin.DataManager, metadata);
            this.send = send; this.broadcast = broadcast;
        }
        internal bool Enqueue(GameCardRequest request) => this.pending.Enqueue(request);
        internal void InventoryChanged(IReadOnlyCollection<InventoryEventArgs> events) => Interlocked.Exchange(ref this.equipmentDirty, 1);
        internal void Tick(CharacterIdentity? owner, string epoch, bool hasSubscribers) {
            this.catalog.RefreshScope();
            this.UpdateEquipment(owner, epoch, hasSubscribers);
            // Waiting requests are bounded with the input queue; at most 32 can be active here.
            for (int i = this.waiting.Count; i < 32; i++) {
                var request = this.pending.Take(); if (request == null) break;
                this.waiting.Add(request);
            }
            if (this.waiting.Any(r => r.Message.Query is CardQuery.Recipes or CardQuery.Sources)) this.catalog.Advance();
            int completed = 0;
            for (int index = 0; index < this.waiting.Count && completed < 4;) {
                var queued = this.waiting[index]; var request = queued.Message;
                if (queued.Cancellation.IsCancellationRequested) { this.waiting.RemoveAt(index); this.pending.Complete(queued); continue; }
                if ((DateTime.UtcNow - queued.EnqueuedAt).TotalSeconds > 45) {
                    this.Reply(queued, new ServerGameCard { Status = CardStatus.Unavailable });
                } else if (request.Query == CardQuery.Equipment) {
                    var validOwner = owner?.Key == request.OwnerKey && epoch == request.OwnerEpoch;
                    this.Reply(queued, new ServerGameCard { Status = validOwner && this.equipment?.IsLive == true ? CardStatus.Success : CardStatus.Stale,
                        Equipment = validOwner ? this.equipment : null, Source = this.metadata.Source(), DataScope = this.metadata.Scope });
                } else {
                    if (request.Query is CardQuery.Recipes or CardQuery.Sources && !this.catalog.Ready && !this.catalog.Failed) { index++; continue; }
                    try { this.Reply(queued, this.catalog.Read(request)); }
                    catch (Exception ex) {
                        Plugin.Log.Warning(ex, "Could not read game card {Item}", request.Id);
                        this.Reply(queued, new ServerGameCard { Status = CardStatus.Unavailable });
                    }
                }
                this.waiting.RemoveAt(index); this.pending.Complete(queued); completed++;
            }
        }
        private void Reply(GameCardRequest request, ServerGameCard reply) {
            reply.RequestId = request.Message.RequestId; reply.Query = request.Message.Query;
            reply.Id = request.Message.Id; reply.ItemKind = request.Message.ItemKind;
            if (!reply.Valid || reply.Encode().Length > CardProtocol.MaxPacketBytes)
                reply = new ServerGameCard { RequestId = request.Message.RequestId, Query = request.Message.Query,
                    Id = request.Message.Id, ItemKind = request.Message.ItemKind, Status = CardStatus.Unavailable };
            if (!request.Cancellation.IsCancellationRequested) this.send(request.ClientId, reply);
        }
        private void UpdateEquipment(CharacterIdentity? owner, string epoch, bool hasSubscribers) {
            if (owner == null) {
                if (this.equipment is { IsLive: true }) {
                    this.equipment.IsLive = false;
                    this.broadcast(new ServerGameCard { Query = CardQuery.Equipment, Equipment = this.equipment, Source = this.metadata.Source() });
                }
                this.equipmentSignature = ""; Interlocked.Exchange(ref this.equipmentDirty, 1); return;
            }
            if (!hasSubscribers) return;
            var now = Environment.TickCount64;
            if (now < this.nextEquipmentRead) return;
            this.nextEquipmentRead = now + 500;
            var job = this.plugin.PlayerState.ClassJob.RowId;
            var level = this.plugin.PlayerState.Level;
            var synced = this.plugin.PlayerState.IsLevelSynced;
            if (Interlocked.Exchange(ref this.equipmentDirty, 0) == 0 && this.equipment?.OwnerEpoch == epoch &&
                this.equipment.ClassJobId == job && this.equipment.Level == level && this.equipment.IsLevelSynced == synced && this.equipment.IsLive && this.equipmentScope == this.metadata.Scope) return;
            try {
                var slots = this.plugin.GameInventory.GetInventoryItems(GameInventoryType.EquippedItems);
                if (slots.Length == 0) { Interlocked.Exchange(ref this.equipmentDirty, 1); return; }
                var items = new List<CardEquippedItem>();
                foreach (ref readonly var slot in slots) {
                    if (slot.IsEmpty || slot.InventorySlot >= 14) continue;
                    var kind = slot.IsCollectable ? ItemKind.Collectible : slot.IsHq ? ItemKind.Hq : ItemKind.Normal;
                    var item = this.metadata.ItemChunk(slot.BaseItemId, kind); if (item == null) continue;
                    var materia = new List<CardMateria>();
                    if (!slot.IsRelic) {
                        var types = slot.Materia; var grades = slot.MateriaGrade;
                        for (int index = 0; index < Math.Min(5, Math.Min(types.Length, grades.Length)); index++) {
                            if (types[index] == 0 || this.plugin.DataManager.GetExcelSheet<Materia>().GetRowOrDefault(types[index]) is not { } row ||
                                grades[index] >= row.Item.Count || grades[index] >= row.Value.Count) continue;
                            var grade = grades[index]; var materiaItem = row.Item[grade].ValueNullable;
                            if (materiaItem == null || materiaItem.Value.RowId == 0) continue;
                            materia.Add(new CardMateria { Slot = index, Item = new CardItemRef { Id = materiaItem.Value.RowId,
                                Name = CardProtocol.Text(materiaItem.Value.Name.ExtractText()), Icon = materiaItem.Value.Icon },
                                ParameterId = row.BaseParam.RowId, ParameterName = CardProtocol.Text(row.BaseParam.ValueNullable?.Name.ExtractText()), Value = row.Value[grade] });
                        }
                    }
                    items.Add(new CardEquippedItem { Slot = (int)slot.InventorySlot, Item = item, Materia = materia.ToArray(), HasCustomStats = slot.IsRelic });
                }
                string signature = epoch + "/" + this.metadata.Scope + "/" + job + "/" + level + "/" + synced + "/" +
                    string.Join(";", items.Select(i => $"{i.Slot}:{i.Item.ItemId}:{i.Item.ItemKind}:{i.HasCustomStats}:" + string.Join(",", i.Materia.Select(m => m.Item.Id))));
                if (signature == this.equipmentSignature) return;
                this.equipmentSignature = signature;
                this.equipmentScope = this.metadata.Scope;
                this.equipment = new EquipmentSnapshot { OwnerKey = owner.Key!, OwnerEpoch = epoch, ClassJobId = job, Level = level,
                    IsLevelSynced = synced, CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), IsLive = true,
                    Items = items.ToArray(), Revision = ++this.revision };
                var reply = new ServerGameCard { Query = CardQuery.Equipment, Equipment = this.equipment, Source = this.metadata.Source(), DataScope = this.metadata.Scope };
                if (reply.Valid && reply.Encode().Length <= CardProtocol.MaxPacketBytes) this.broadcast(reply);
            } catch (Exception ex) {
                Interlocked.Exchange(ref this.equipmentDirty, 1);
                this.nextEquipmentRead = now + 5_000;
                Plugin.Log.Warning(ex, "Could not read equipped-item snapshot");
            }
        }
        public void Dispose() { this.pending.Clear(); this.waiting.Clear(); this.catalog.Dispose(); }
    }
}
