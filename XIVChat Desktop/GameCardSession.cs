using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed record CardOrigin(string Source, string OwnerKey, ServerMessage? Message = null, string? FavoriteId = null);
    public sealed record CardLoad(ServerGameCard? Page, bool Cached, CardStatus Status);
    public sealed class GameCardSession {
        private readonly App app;
        private readonly Dictionary<(string Source, string Owner), EquipmentSnapshot> equipment = new();
        private string activeContext = "";
        private int contextVersion;
        public event Action? EquipmentChanged;
        public event Action? FavoritesChanged;
        public ChineseGameText Chinese { get; }
        public GameCardSession(App app, ChineseGameText? chinese = null) {
            this.app = app;
            this.Chinese = chinese ?? new ChineseGameText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChatDesktop", "GameCards", "Chinese"));
        }
        public CardOrigin Origin(ServerMessage? message = null) {
            string source = message?.LocalSource ?? this.app.Workbench.Source;
            if (string.IsNullOrEmpty(source)) source = this.app.Session.Source;
            string owner = message != null ? message.Owner?.Key ?? "unassigned:" + source : this.app.Workbench.OwnerKey ?? "unassigned:" + source;
            return new(source, owner, message);
        }
        public void UpdateContext(Connection connection) {
            if (!ReferenceEquals(this.app.Connection, connection)) return;
            var player = this.app.Session.Player;
            string key = connection.Id + "/" + player?.Identity?.Key + "/" + player?.OwnerEpoch + "/" + connection.SupportsGameCards + "/" + connection.Available;
            if (key == this.activeContext) return;
            this.MarkStale(connection); this.activeContext = key; int version = ++this.contextVersion;
            if (connection.SupportsGameCards && connection.Available && player?.Identity?.Key != null && player.OwnerEpoch != null)
                _ = this.RefreshEquipmentAsync(connection, player.Identity.Key, player.OwnerEpoch, version);
        }
        public void MarkStale(Connection connection) {
            foreach (var pair in this.equipment.Where(p => p.Key.Source == connection.Source)) pair.Value.IsLive = false;
            this.EquipmentChanged?.Invoke();
        }
        public void Disconnect(Connection connection) {
            this.MarkStale(connection); this.activeContext = ""; this.contextVersion++;
        }
        private async Task RefreshEquipmentAsync(Connection connection, string owner, string epoch, int version) {
            try {
                var reply = await connection.RequestCardAsync(new ClientGameCard { Query = CardQuery.Equipment, OwnerKey = owner, OwnerEpoch = epoch }, connection.cancel.Token);
                if (version == this.contextVersion && reply.Equipment != null) this.ObserveEquipment(connection, reply.Equipment);
            } catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { }
        }
        public void ObserveEquipment(Connection connection, EquipmentSnapshot snapshot) {
            var player = this.app.Session.Player;
            if (snapshot.IsLive && !connection.Available) return;
            if (!ReferenceEquals(this.app.Connection, connection) || player?.Identity?.Key != snapshot.OwnerKey ||
                player.OwnerEpoch != snapshot.OwnerEpoch || !CardProtocol.ValidEquipment(snapshot)) return;
            var key = (connection.Source, snapshot.OwnerKey);
            if (this.equipment.TryGetValue(key, out var previous) && previous.OwnerEpoch == snapshot.OwnerEpoch && previous.Revision > snapshot.Revision) return;
            this.equipment[key] = snapshot;
            if (this.equipment.Count > 32) this.equipment.Remove(this.equipment.OrderBy(p => p.Value.CapturedAtUnixMilliseconds).First().Key);
            this.EquipmentChanged?.Invoke();
            if (this.app.Config.HistoryEnabled && this.app.Session.Store is { } store) _ = this.PersistEquipment(store, connection.Source, snapshot);
        }
        private async Task PersistEquipment(HistoryStore store, string source, EquipmentSnapshot snapshot) {
            try { await store.SaveEquipmentAsync(source, snapshot); } catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
        }
        public async Task<EquipmentSnapshot?> EquipmentAsync(CardOrigin origin) {
            var key = (origin.Source, origin.OwnerKey);
            if (this.equipment.TryGetValue(key, out var current)) return current;
            if (this.app.Session.Store is not { } store) return null;
            try {
                var cached = await store.GetEquipmentAsync(origin.Source, origin.OwnerKey);
                if (this.equipment.TryGetValue(key, out current)) return current;
                if (cached != null) {
                    this.equipment[key] = cached;
                    if (this.equipment.Count > 32) this.equipment.Remove(this.equipment.OrderBy(p => p.Value.CapturedAtUnixMilliseconds).First().Key);
                }
                return cached;
            } catch (Exception ex) { this.app.Session.ReportStorageError(ex); return null; }
        }
        public async Task<CardLoad> LoadAsync(CardOrigin origin, CardQuery query, uint id, uint kind, int page, string? scope,
            GameDataSource? expected, CancellationToken token) {
            ServerGameCard? cached = null;
            if (this.app.Session.Store is { } store) {
                try { cached = await store.GetCardPageAsync(origin.Source, query, id, kind, page, expected?.Version, expected?.Language, scope); }
                catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
            }
            token.ThrowIfCancellationRequested();
            var connection = this.app.Connection;
            if (connection?.SupportsGameCards == true && connection.Source == origin.Source && !connection.cancel.IsCancellationRequested) {
                try {
                    var reply = await connection.RequestCardAsync(new ClientGameCard { Query = query, Id = id, ItemKind = kind, Page = page, DataScope = scope }, token);
                    token.ThrowIfCancellationRequested();
                    if (reply.Status == CardStatus.Success) {
                        if (this.app.Session.Store is { } cache && reply.Source != null) {
                            try { await cache.SaveCardPageAsync(origin.Source, reply); } catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
                        }
                        return new(reply, false, reply.Status);
                    }
                    return new(cached, cached != null, reply.Status);
                } catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { token.ThrowIfCancellationRequested(); }
            }
            return new(cached, cached != null, cached != null ? CardStatus.Success : CardStatus.Unavailable);
        }
        public async Task<CardFavorite> FavoriteAsync(CardOrigin origin, TextChunk snapshot, bool map, string name) {
            var store = this.app.Session.Store ?? throw new InvalidOperationException(LocalizationHelper.GetString("History.Unavailable"));
            string identity = map ? $"map/{snapshot.MapId}/{snapshot.MapX?.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}/{snapshot.MapY?.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}" : $"item/{snapshot.ItemId}/{snapshot.ItemKind}";
            var favorite = new CardFavorite { Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin.Source + "\n" + origin.OwnerKey + "\n" + identity))),
                Source = origin.Source, OwnerKey = origin.OwnerKey, Name = CardProtocol.Text(name, 512), Snapshot = snapshot,
                IsMap = map, SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            string? messageId = origin.Message?.LocalStorageId;
            if (origin.Message is { } message && (message.Owner?.Key ?? "unassigned:" + origin.Source) == origin.OwnerKey) {
                messageId ??= HistoryStore.StorageId(origin.Source, message); message.LocalStorageId = messageId;
                await store.AppendAsync(origin.Source, message, messageId);
            }
            await store.SaveCardFavoriteAsync(favorite, messageId); this.FavoritesChanged?.Invoke(); return favorite;
        }
    }
}
