using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed class ConversationModel : INotifyPropertyChanged {
        public ConversationState State { get; private set; }
        public CharacterIdentity Peer => State.Peer;
        public string Key => State.PeerKey;
        public string Name => Peer.Name;
        public string World => Peer.HomeWorld;
        public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0]));
        public string Preview => Draft.Length > 0 ? LocalizationHelper.GetString("Conversation.Draft") + ": " + Draft : Latest?.ContentText ?? Note;
        public string Time => Latest?.Timestamp.ToLocalTime().ToString("HH:mm") ?? "";
        public ServerMessage? Latest { get; private set; }
        public int Unread { get; private set; }
        public string Badge => Unread > 0 ? (Unread > 99 ? "99+" : Unread.ToString()) : Pinned ? "↑" : "";
        public bool Pinned { get => State.Pinned; set { State = State with { Pinned = value }; Dirty = locallyEdited = true; Notify(); } }
        public string Draft { get => State.Draft; set { State = State with { Draft = value }; Dirty = locallyEdited = true; Notify(); } }
        public string Note { get => State.Note; set { State = State with { Note = value }; Dirty = locallyEdited = true; Notify(); } }
        private bool locallyEdited;
        public bool Dirty { get; internal set; }
        public string SendStatus { get; private set; } = "";
        public string? FailedDraft { get; private set; }
        public ConversationModel(ConversationState state) => State = state;
        public void Restore(ConversationSnapshot snapshot) {
            if (!locallyEdited) State = snapshot.State;
            if (snapshot.Latest != null && (Latest == null || ChatSession.Compare(snapshot.Latest.Message, Latest) > 0)) Latest = snapshot.Latest.Message;
            Unread = Math.Max(Unread, snapshot.Unread); Notify(nameof(Draft));
        }
        public void Observe(ServerMessage message, bool live) {
            if (Latest == null || ChatSession.Compare(message, Latest) > 0) Latest = message;
            if (live && ((ushort)message.Channel & 127) == (ushort)ChatType.TellIncoming) Unread = Math.Min(9999, Unread + 1);
            Notify();
        }
        public void MarkRead() { Unread = 0; Notify(); }
        public void UpdatePeer(CharacterIdentity peer) { State = State with { Peer = ConversationIdentity.Copy(peer) }; Notify(); }
        public void SetStatus(string status, string? failed = null) { SendStatus = status; FailedDraft = failed; Notify(); }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? property = null) {
            if (property == nameof(Draft)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Draft)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }
    }

    /// <summary>Shared UI-thread conversation state. Storage and protocol replies keep their original source and owner.</summary>
    public sealed class WorkbenchSession {
        private readonly App app;
        private readonly Dictionary<string, ConversationModel> byPeer = new();
        private readonly Dictionary<string, (Connection Connection, ConversationModel? Conversation, string Text, Action<string, string?, bool>? Reply)> pending = new();
        private readonly Dictionary<ConversationModel, CancellationTokenSource> draftSaves = new();
        private readonly HashSet<Task> writes = new();
        private readonly HashSet<ConversationModel> failedWrites = new();
        private Connection? subscribed;
        private string observedSource;
        private int version;
        public ObservableCollection<ConversationModel> Conversations { get; } = new();
        public string Source { get; private set; } = "";
        public string? OwnerKey { get; private set; }
        public CharacterIdentity? Owner { get; private set; }
        public bool Loading { get; private set; }
        public string? Error { get; private set; }
        public event Action? Changed;
        public WorkbenchSession(App app) {
            this.app = app; this.observedSource = app.Session.Source;
            app.Session.ContextChanged += ContextChanged;
            app.Session.MessagesChanged += MessagesChanged;
            app.Session.Friends.Changed += FriendsChanged;
            app.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(App.Connection)) ConnectionChanged(); };
            ConnectionChanged(); ContextChanged();
        }

        private void ContextChanged() {
            if (observedSource != app.Session.Source) {
                observedSource = app.Session.Source;
                SetContext(observedSource, null);
                return;
            }
            var player = app.Session.Player;
            if (player?.Identity?.Key is { } owner && (owner != OwnerKey || Source != app.Session.Source))
                SetContext(app.Session.Source, player.Identity);
            Changed?.Invoke();
        }
        public void SetContext(string source, CharacterIdentity? owner) {
            foreach (var model in Conversations.Where(c => c.Dirty).ToArray()) Save(model);
            Source = source; Owner = owner == null ? null : ConversationIdentity.Copy(owner); OwnerKey = owner?.Key;
            version++; byPeer.Clear(); Conversations.Clear(); Error = null;
            if (app.Session.Player == null) app.Session.Friends.SetContext(source, OwnerKey, null, false);
            if (OwnerKey != null) _ = RestoreAsync(version, source, OwnerKey);
            Changed?.Invoke();
        }
        private async Task RestoreAsync(int expected, string source, string owner) {
            var store = app.Session.Store;
            if (store == null) return;
            Loading = true; Changed?.Invoke();
            try {
                var saved = await store.GetConversationsAsync(source, owner);
                if (expected != version) return;
                foreach (var item in saved) {
                    var model = GetOrCreate(item.State.Peer);
                    model?.Restore(item);
                }
                Sort(); FriendsChanged();
                if (app.Session.Player == null && app.Session.Friends.Source == source && app.Session.Friends.OwnerKey == owner) {
                    var friendVersion = app.Session.Friends.Version;
                    var snapshot = await store.GetFriendSnapshotAsync(source, owner);
                    if (expected == version) app.Session.Friends.Restore(friendVersion, snapshot);
                }
            } catch (Exception ex) { if (expected == version) Error = ex.Message; }
            finally { if (expected == version) { Loading = false; Changed?.Invoke(); } }
        }
        public ConversationModel? Open(CharacterIdentity peer) {
            var model = GetOrCreate(peer);
            if (model != null) { Save(model); Sort(); Changed?.Invoke(); }
            return model;
        }
        private ConversationModel? GetOrCreate(CharacterIdentity peer) {
            var key = ConversationIdentity.PeerKey(peer);
            if (OwnerKey == null || key == null) return null;
            if (byPeer.TryGetValue(key, out var model)) return model;
            model = failedWrites.FirstOrDefault(c => c.State.Source == Source && c.State.OwnerKey == OwnerKey && c.Key == key)
                ?? new ConversationModel(new ConversationState(Source, OwnerKey, key, ConversationIdentity.Copy(peer)));
            byPeer.Add(key, model); Conversations.Add(model); return model;
        }
        private void MessagesChanged(ServerMessage[] messages, bool live) {
            if (Source != app.Session.Source || OwnerKey == null) return;
            var changed = false;
            foreach (var message in messages) {
                if (message.Owner?.Key != OwnerKey || !ConversationIdentity.IsTell((ushort)message.Channel) || message.TellPeer == null) continue;
                var model = GetOrCreate(message.TellPeer);
                if (model == null) continue;
                model.Observe(message, live); changed = true;
            }
            if (changed) { Sort(); Changed?.Invoke(); }
        }
        private void FriendsChanged() {
            var friends = app.Session.Friends;
            if (friends.Source != Source || friends.OwnerKey != OwnerKey || friends.IsStale || friends.Snapshot == null) { Changed?.Invoke(); return; }
            foreach (var model in Conversations) {
                var matches = friends.Snapshot.Players.Where(p => !p.IdentityUnavailable && p.HomeWorld == model.Peer.HomeWorldId &&
                    string.Equals(p.Name, model.Peer.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || model.Peer.ContentId != 0 && model.Peer.ContentId != matches[0].ContentId) continue;
                var peer = ConversationIdentity.Copy(model.Peer); peer.ContentId = matches[0].ContentId;
                model.UpdatePeer(peer);
            }
            Changed?.Invoke();
        }
        public void Sort() {
            var ordered = Conversations.OrderByDescending(c => c.Pinned).ThenByDescending(c => c.Latest?.Timestamp ?? DateTime.MinValue).ThenBy(c => c.Name).ToArray();
            for (var i = 0; i < ordered.Length; i++) { var old = Conversations.IndexOf(ordered[i]); if (old != i) Conversations.Move(old, i); }
        }
        public void Save(ConversationModel model, bool debounce = false) {
            if (draftSaves.Remove(model, out var old)) { old.Cancel(); old.Dispose(); }
            if (debounce) {
                var stop = new CancellationTokenSource(); draftSaves[model] = stop;
                _ = DelaySaveAsync(model, stop); return;
            }
            var state = model.State;
            if (app.Session.Store == null) return;
            model.Dirty = false;
            Track(SaveAsync(model, state));
        }
        private async Task DelaySaveAsync(ConversationModel model, CancellationTokenSource stop) {
            try { await Task.Delay(400, stop.Token); if (draftSaves.TryGetValue(model, out var active) && active == stop) Save(model); }
            catch (OperationCanceledException) { }
        }
        private async Task SaveAsync(ConversationModel model, ConversationState state) {
            try { if (app.Session.Store is { } store) { await store.SaveConversationAsync(state); failedWrites.Remove(model); } }
            catch (Exception ex) { model.Dirty = true; failedWrites.Add(model); Error = ex.Message; Changed?.Invoke(); }
        }
        private async void Track(Task task) { writes.Add(task); try { await task; } finally { writes.Remove(task); } }
        public Task MarkReadAsync(ConversationModel model) { var task = PersistReadAsync(model); Track(task); return task; }
        private async Task PersistReadAsync(ConversationModel model) {
            model.MarkRead();
            var latest = model.Latest;
            var store = app.Session.Store;
            if (latest?.MessageId == null || store == null) return;
            try {
                var row = await store.GetMessageAsync(HistoryStore.StorageId(model.State.Source, latest));
                if (row != null) await store.MarkConversationReadAsync(model.State.Source, model.State.OwnerKey, model.Key, row.Message.Timestamp, row.RowId);
            } catch (Exception ex) { Error = ex.Message; Changed?.Invoke(); }
        }
        public bool Send(ConversationModel model, string text, Action<string, string?, bool>? reply = null) {
            var conn = app.Connection;
            var player = app.Session.Player;
            if (conn == null || model.State.Source != app.Session.Source || model.State.OwnerKey != player?.Identity?.Key ||
                player.OwnerEpoch == null || pending.Count >= 128) return false;
            var id = conn.SendTell(TellTarget.From(model.Peer), text, model.State.OwnerKey, player.OwnerEpoch);
            if (id == null) return false;
            pending[id] = (conn, model, text, reply);
            if (reply == null) { model.Draft = ""; Save(model); model.SetStatus(LocalizationHelper.GetString("Conversation.Queued")); }
            else reply(LocalizationHelper.GetString("Conversation.Queued"), null, false);
            _ = ExpireSendAsync(id);
            return true;
        }
        public bool SendChannel(string source, string owner, string text, Action<string, string?, bool> reply) {
            var conn = app.Connection;
            if (conn == null || source != app.Session.Source || owner != (app.Session.Player?.Identity?.Key ?? "unassigned:" + source) || pending.Count >= 128) return false;
            var id = conn.SendMessageWithId(text);
            if (id == null) return false;
            if (!conn.SupportsGuardedCommands) { reply(LocalizationHelper.GetString("Conversation.Unknown"), null, true); return true; }
            pending[id] = (conn, null, text, reply);
            reply(LocalizationHelper.GetString("Conversation.Queued"), null, false);
            _ = ExpireSendAsync(id); return true;
        }
        private async Task ExpireSendAsync(string id) {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (pending.Remove(id, out var item)) Fail(item.Conversation, item.Text, LocalizationHelper.GetString("Conversation.Unknown"), item.Reply);
        }
        private void ConnectionChanged() {
            if (subscribed != null) subscribed.CommandResult -= OnCommandResult;
            foreach (var item in pending.Values) Fail(item.Conversation, item.Text, LocalizationHelper.GetString("Command.Disconnected"), item.Reply);
            pending.Clear(); subscribed = app.Connection;
            if (subscribed != null) subscribed.CommandResult += OnCommandResult;
            Changed?.Invoke();
        }
        private void OnCommandResult(ServerCommandResult result) {
            if (result.RequestId == null || !pending.TryGetValue(result.RequestId, out var item)) return;
            if (result.Stage == CommandStage.Rejected) {
                pending.Remove(result.RequestId);
                var reason = LocalizationHelper.GetString("Command." + result.Failure);
                if (result.SubmittedParts > 0) reason = string.Format(LocalizationHelper.GetString("Conversation.Partial"), result.SubmittedParts) + " " + reason;
                Fail(item.Conversation, item.Text, reason, item.Reply);
            } else {
                var status = LocalizationHelper.GetString(result.Stage == CommandStage.Submitted ? "Conversation.Submitted" : "Conversation.Queued");
                if (item.Reply != null) item.Reply(status, null, result.Stage == CommandStage.Submitted); else item.Conversation?.SetStatus(status);
                if (result.Stage == CommandStage.Submitted) pending.Remove(result.RequestId);
            }
        }
        private void Fail(ConversationModel? model, string text, string reason, Action<string, string?, bool>? reply = null) {
            if (reply != null) { reply(reason, text, true); return; }
            if (model == null) return;
            if (string.IsNullOrEmpty(model.Draft)) { model.Draft = text; Save(model); model.SetStatus(reason); }
            else model.SetStatus(reason, text);
        }
        public async Task FlushAsync() {
            foreach (var model in draftSaves.Keys.Concat(Conversations.Where(c => c.Dirty)).Concat(failedWrites).Distinct().ToArray()) Save(model);
            if (writes.Count > 0) await Task.WhenAll(writes.ToArray());
        }
    }
}
