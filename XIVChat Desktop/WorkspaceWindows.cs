using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using XIVChatStorage;

namespace XIVChat_Desktop;

/// <summary>Owns chat-window lifetime; auxiliary windows do not keep the connection alive.</summary>
public sealed class WorkspaceWindows {
    private readonly App app;
    private readonly Dictionary<string, ChatPopoutWindow> windows = new();
    private WorkspaceLayout layout = new();
    private readonly SemaphoreSlim saves = new(1);
    private CancellationTokenSource? debounce;
    private DisplayAreaWatcher? displays;
    private bool initialized, applying, saveAllowed = true;
    public bool Stopping { get; private set; }
    public bool MainVisible => layout.MainVisible;
    public IReadOnlyCollection<ChatPopoutWindow> Popouts => windows.Values;
    public string? Error { get; private set; }
    private static string L(string key) => LocalizationHelper.GetString(key);
    public WorkspaceWindows(App app) => this.app = app;

    public async Task RestoreAsync() {
        try {
            var saved = app.Session.Store == null ? null : await app.Session.Store.LoadLayoutAsync("current");
            if (saved != null) {
                foreach (var state in saved.Windows) if (state.PendingDraft is { } pending) {
                    if (state.Draft.Length == 0) state.Draft = pending; else state.FailedDraft = pending;
                    state.PendingDraft = null;
                }
                await ApplyAsync(saved);
            }
        } catch (Exception ex) {
            // Keep an unreadable saved layout intact; this session can still use temporary windows.
            saveAllowed = false; Report(ex);
        } finally { initialized = true; }
        app.Window.AppWindow.Changed += (_, e) => { if (e.DidPositionChange || e.DidSizeChange) ScheduleSave(); };
        displays = DisplayArea.CreateWatcher();
        displays.Updated += (_, _) => FitAll(); displays.Removed += (_, _) => FitAll(); displays.Start();
        if (app.Window.Content is FrameworkElement root) {
            void WatchRoot() {
                if (root.XamlRoot == null) return;
                double scale = root.XamlRoot.RasterizationScale;
                root.XamlRoot.Changed += (_, _) => {
                    if (root.XamlRoot.RasterizationScale == scale) return;
                    scale = root.XamlRoot.RasterizationScale; FitAll();
                };
            }
            if (root.IsLoaded) WatchRoot(); else root.Loaded += (_, _) => WatchRoot();
        }
    }
    public ChatPopoutWindow? Open(ChatWindowState requested) {
        if (Stopping || !requested.Valid) return null;
        if (windows.TryGetValue(requested.Key, out var existing)) { existing.BringForward(); return existing; }
        if (windows.Count >= 12) { Report(new InvalidOperationException(L("Windows.Limit"))); return null; }
        var state = layout.Windows.FirstOrDefault(s => s.Key == requested.Key);
        if (state == null) {
            // Never evict unsent drafts to make room for another view.
            if (layout.Windows.Count >= 64) {
                var discard = layout.Windows.FirstOrDefault(s => !s.Open && s.Draft.Length == 0 && s.FailedDraft == null && s.PendingDraft == null);
                if (discard == null) { Report(new InvalidOperationException(L("Windows.Limit"))); return null; }
                layout.Windows.Remove(discard);
            }
            state = requested; layout.Windows.Add(state);
        }
        var window = new ChatPopoutWindow(app, this, state);
        windows.Add(state.Key, window); state.Open = true;
        window.Activate(); ApplyBounds(window, state.Bounds);
        ScheduleSave(); return window;
    }
    public void ShowMain() {
        if (Stopping) return;
        layout.MainVisible = true;
        if (app.Window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        app.Window.SetWorkspaceVisible(true);
        ApplyBounds(app.Window, layout.MainBounds); app.Window.Activate(); ScheduleSave();
    }
    public async Task CloseMainAsync() {
        if (Stopping) return;
        app.Window.CaptureWorkspaceComposer();
        if (windows.Count == 0) { await ShutdownAsync(); return; }
        if (IsNormal(app.Window)) layout.MainBounds = BoundsOf(app.Window); layout.MainVisible = false;
        app.Window.SetWorkspaceVisible(false); await SaveAsync();
    }
    public async Task ClosePopoutAsync(ChatPopoutWindow window) {
        if (Stopping) return;
        if (!MainVisible && windows.Count == 1) { await ShutdownAsync(); return; }
        window.Capture(); window.State.Open = false;
        windows.Remove(window.State.Key); window.CloseForWorkspace(); await SaveAsync();
    }
    public async Task ShutdownAsync() {
        if (Stopping) return;
        Capture(); Stopping = true; debounce?.Cancel(); displays?.Stop();
        app.Window.CaptureWorkspaceComposer();
        try { await app.StopSessionAsync(); }
        finally {
            foreach (var window in windows.Values.ToArray()) window.CloseForWorkspace();
            windows.Clear(); ThemeHelper.CloseAuxiliaryWindows(app.Window); app.Window.CloseForWorkspace(); app.Exit();
        }
    }
    public WorkspaceLayout Capture() {
        if (app.Window != null && layout.MainVisible && IsNormal(app.Window)) layout.MainBounds = BoundsOf(app.Window);
        foreach (var window in windows.Values) window.Capture();
        app.Window?.CaptureWorkspaceState(layout);
        return WorkspaceLayout.Parse(layout.Serialize());
    }
    public void ScheduleSave() {
        if (!initialized || applying || Stopping) return;
        debounce?.Cancel(); debounce?.Dispose(); debounce = new();
        _ = SaveLaterAsync(debounce.Token);
    }
    private async Task SaveLaterAsync(CancellationToken token) {
        try { await Task.Delay(500, token); await SaveAsync(); }
        catch (OperationCanceledException) { }
    }
    public async Task SaveAsync(string id = "current") {
        if (app.Session.Store is not { } store || id == "current" && !saveAllowed || applying) return;
        WorkspaceLayout snapshot;
        try { snapshot = Capture(); } catch (Exception ex) { Report(ex); return; }
        await saves.WaitAsync();
        try { await store.SaveLayoutAsync(id, snapshot); Error = null; }
        catch (Exception ex) { Report(ex); }
        finally { saves.Release(); }
    }
    public async Task LoadNamedAsync(string id) {
        if (app.Session.Store is not { } store) return;
        try {
            var saved = await store.LoadLayoutAsync(id);
            if (saved != null) { await ApplyAsync(saved); await SaveAsync(); }
        } catch (Exception ex) { Report(ex); }
    }
    public async Task ApplyAsync(WorkspaceLayout saved) {
        // A layout changes positions/open views; it never rolls drafts back to an older snapshot.
        WorkspaceLayout.Parse(saved.Serialize()); if (initialized) Capture();
        applying = true;
        try {
            var drafts = layout.Windows.ToDictionary(s => s.Key);
            var incoming = WorkspaceLayout.Parse(saved.Serialize());
            foreach (var draft in layout.MainDrafts) incoming.MainDrafts[draft.Key] = draft.Value;
            foreach (var state in incoming.Windows) if (drafts.TryGetValue(state.Key, out var current)) {
                state.Draft = current.Draft; state.FailedDraft = current.FailedDraft; state.PendingDraft = current.PendingDraft;
            }
            foreach (var state in drafts.Values.Where(s => incoming.Windows.All(w => w.Key != s.Key))) {
                if (incoming.Windows.Count >= 64) throw new InvalidOperationException(L("Windows.Limit"));
                incoming.Windows.Add(state with { Open = false });
            }
            foreach (var window in windows.Values.ToArray()) window.CloseForWorkspace();
            windows.Clear();
            layout = incoming;
            app.Window.RestoreWorkspaceState(layout);
            foreach (var state in layout.Windows.Where(s => s.Open).ToArray()) Open(state);
            if (windows.Count == 0) layout.MainVisible = true;
            ApplyBounds(app.Window, layout.MainBounds); app.Window.SetWorkspaceVisible(layout.MainVisible);
            if (layout.MainVisible) app.Window.Activate();
        } finally { applying = false; }
        await Task.CompletedTask;
    }
    public void ApplyPreset(int preset) {
        ShowMain();
        if (windows.Count == 0) app.Window.PopoutCurrent();
        var work = WorkArea(BoundsOf(app.Window));
        var mainWidth = (int)(work.Width * (preset == 1 ? .5 : preset == 2 ? .58 : .65));
        ApplyBounds(app.Window, new(work.X, work.Y, mainWidth, work.Height));
        var items = windows.Values.ToArray();
        for (int i = 0; i < items.Length; i++) {
            var columns = preset == 2 && items.Length > 2 ? 2 : 1;
            var rows = (int)Math.Ceiling(items.Length / (double)columns);
            var width = (work.Width - mainWidth) / columns;
            var height = work.Height / Math.Max(1, rows);
            ApplyBounds(items[i], new(work.X + mainWidth + i % columns * width, work.Y + i / columns * height, width, height));
        }
        ScheduleSave();
    }
    public void SendReply(string key, string status, string? failed, bool complete) {
        var state = layout.Windows.FirstOrDefault(s => s.Key == key);
        if (state == null) return;
        if (complete) state.PendingDraft = null;
        if (failed != null) {
            if (state.Draft.Length == 0) state.Draft = failed; else state.FailedDraft = failed;
        }
        if (windows.TryGetValue(key, out var window)) window.ShowSendReply(status);
        ScheduleSave();
    }
    public bool IsReading(NotificationTarget target) => MainVisible && app.Window?.IsReadingNotification(target) == true || windows.Values.Any(w => w.IsReading(target));
    public Task OpenNotificationAsync(NotificationTarget target) {
        var window = windows.Values.FirstOrDefault(w => w.Matches(target));
        if (window != null) return window.OpenNotificationAsync(target);
        ShowMain(); return app.Window.OpenNotificationTargetAsync(target);
    }
    private void Report(Exception ex) { Error = ex.Message; app.Window?.AddSystemMessage(L("Windows.SaveFailed") + " " + ex.Message); foreach (var window in windows.Values) window.ShowWorkspaceError(ex.Message); }
    private void FitAll() => app.Dispatch(() => {
        if (Stopping || !initialized) return;
        if (IsNormal(app.Window)) ApplyBounds(app.Window, BoundsOf(app.Window));
        foreach (var window in windows.Values) if (IsNormal(window)) ApplyBounds(window, BoundsOf(window));
        ScheduleSave();
    });
    public static bool IsNormal(Window window) => window.AppWindow.Presenter is not OverlappedPresenter p || p.State == OverlappedPresenterState.Restored;
    public static WindowBounds BoundsOf(Window window) => new(window.AppWindow.Position.X, window.AppWindow.Position.Y, window.AppWindow.Size.Width, window.AppWindow.Size.Height);
    public static WindowBounds WorkArea(WindowBounds bounds) {
        var display = DisplayArea.GetFromRect(new RectInt32(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)), DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        return new(area.X + display.OuterBounds.X, area.Y + display.OuterBounds.Y, area.Width, area.Height);
    }
    public static void ApplyBounds(Window window, WindowBounds bounds) {
        var fit = bounds.Fit(WorkArea(bounds));
        if (BoundsOf(window) != fit) window.AppWindow.MoveAndResize(new RectInt32(fit.X, fit.Y, fit.Width, fit.Height));
    }
}
