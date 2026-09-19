using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using XIVChatCommon.Presentation;

namespace XIVChat_Desktop;

/// <summary>Application-owned display state shared by every window.</summary>
public sealed class IdentityPresentation {
    private readonly App app;
    private readonly HashSet<Task> writes = new();
    private string settingsKey = "";
    private bool queued;
    public IdentityDisplay Engine { get; }
    public event Action? Changed;
    public event Action? PolicyChanged;
    public string? Error { get; private set; }
    public bool Enabled => app.Config.Privacy.Enabled;
    public IdentityPresentation(App app) {
        this.app = app;
        Engine = new(app.Config.Privacy, LocalizationHelper.LanguageCode == "zh-CN", Util.WorldName);
        Engine.ProfileGenerated += profile => {
            if (app.Session.Store is { } store) Track(store.SaveContactPseudonymAsync(profile));
        };
        app.Config.Saved += Configure;
        LocalizationHelper.LanguageChanged += Configure;
        app.Session.MessagesChanged += (messages, _) => { foreach (var message in messages) Observe(message); };
        app.Session.ContextChanged += () => { Engine.Context(app.Session.Source, app.Session.Player?.Identity?.Key, app.Session.Player?.Identity); Refresh(); };
        app.Session.Friends.Changed += RegisterFriends;
        Configure();
    }
    public async Task InitializeAsync() {
        if (app.Session.Store is not { } store) return;
        try {
            foreach (var owner in await store.GetOwnersAsync()) Engine.Context(owner.Source, owner.OwnerKey, owner.Identity);
            foreach (var profile in await store.GetContactDisplayProfilesAsync()) Engine.Restore(profile);
        } catch (Exception ex) { Error = ex.Message; }
    }
    private void Configure() {
        var p = app.Config.Privacy;
        var key = $"{p.Enabled}/{p.HideSelf}/{p.HideOthers}/{p.SelfName}/{LocalizationHelper.LanguageCode}";
        if (key == settingsKey) return;
        settingsKey = key; Engine.Configure(p, LocalizationHelper.LanguageCode == "zh-CN");
        PolicyChanged?.Invoke(); Changed?.Invoke();
    }
    public void Refresh() => Changed?.Invoke();
    private void QueueRefresh() {
        if (queued) return;
        queued = true;
        app.Dispatch(() => { queued = false; Refresh(); });
    }
    private async void Track(Task task) {
        writes.Add(task);
        try { await task; }
        catch (Exception ex) { Error = ex.Message; QueueRefresh(); }
        finally { writes.Remove(task); }
    }
    public async Task FlushAsync() {
        while (writes.Count > 0) {
            try { await Task.WhenAll(writes.ToArray()); }
            catch (Exception ex) { Error = ex.Message; }
        }
    }
    public DisplayContext Current => Engine.Context(app.Workbench.Source, app.Workbench.OwnerKey, app.Workbench.Owner);
    public DisplayContext Context(string source, string? owner, CharacterIdentity? identity = null) => Engine.Context(source, owner, identity);
    public DisplayContext Context(ServerMessage message) {
        var source = message.LocalSource ?? app.Session.Source;
        return Engine.Context(source, message.Owner?.Key, message.Owner);
    }
    public DisplayContext Observe(ServerMessage message) {
        var revision = Engine.Revision;
        var context = Engine.Observe(message.LocalSource ?? app.Session.Source, message);
        if (Engine.Sender(context, message) is { HomeWorldId: > 0, HomeWorld.Length: 0 } sender) {
            sender = ConversationIdentity.Copy(sender); sender.HomeWorld = Util.WorldName(sender.HomeWorldId) ?? "";
            Engine.Register(context, sender);
        }
        if (revision != Engine.Revision && Enabled) QueueRefresh();
        return context;
    }
    private void RegisterFriends() {
        var state = app.Session.Friends;
        if (state.Snapshot is not { } snapshot) return;
        var context = Engine.Context(state.Source, state.OwnerKey, snapshot.Owner);
        foreach (var friend in snapshot.Players.Where(p => !p.IdentityUnavailable))
            Engine.Register(context, new CharacterIdentity { Name = friend.Name ?? "", HomeWorldId = friend.HomeWorld,
                HomeWorld = friend.HomeWorldName ?? Util.WorldName(friend.HomeWorld) ?? "", ContentId = friend.ContentId });
        Refresh();
    }
    public DisplayIdentity Identity(CharacterIdentity? peer, DisplayContext? context = null) => Engine.Identity(context ?? Current, peer);
    public string Text(string? text, DisplayContext? context = null) => Engine.Text(context ?? Current, text);
    public string Content(ServerMessage message) { Observe(message); return Engine.Content(message.LocalSource ?? app.Session.Source, message); }
    public string Sender(ServerMessage message) => Engine.SenderLabel(Observe(message), message);
    public bool Hidden(CharacterIdentity? peer, DisplayContext? context = null) => Engine.Hidden(context ?? Current, peer);
    public IReadOnlyList<Chunk> Chunks(ServerMessage message) { Observe(message); return Engine.Chunks(message.LocalSource ?? app.Session.Source, message); }
    public string OwnerLabel(string source, string ownerKey, CharacterIdentity? owner = null) =>
        Engine.Identity(Context(source, ownerKey, owner), owner ?? Context(source, ownerKey).Owner).Label;
    public async Task SetNicknameAsync(DisplayContext context, CharacterIdentity peer, string nickname) {
        if (app.Session.Store is not { } store) throw new InvalidOperationException(LocalizationHelper.GetString("History.Unavailable"));
        var profile = Engine.WithNickname(context, peer, nickname);
        await store.SaveContactNicknameAsync(profile);
        // A pseudonym may have been generated while the asynchronous write was pending.
        Engine.Restore(Engine.WithNickname(context, peer, profile.Nickname));
        Error = null; Refresh();
    }
    public string EventDetails(string source, ServerGameEvent entry) {
        var context = Context(source, entry.Owner?.Key, entry.Owner);
        if (entry.Kind is GameEventKind.Login or GameEventKind.Logout) return Identity(entry.Owner, context).Label;
        return Text(NotificationCenter.EventDetails(entry), context);
    }
}
