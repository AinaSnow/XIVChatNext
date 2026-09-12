using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XIVChat_Desktop;

public sealed class WorkspaceLayoutWindow : Window {
    private readonly App app;
    private readonly ListView saved = new() { MinHeight = 120, MaxHeight = 240 };
    private readonly TextBox name = new() { MaxLength = 100 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private bool closed;
    private static string L(string key) => LocalizationHelper.GetString(key);
    public WorkspaceLayoutWindow(App app) {
        this.app = app;
        var panel = new StackPanel { Padding = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = L("Windows.PresetHelp"), TextWrapping = TextWrapping.Wrap });
        foreach (var (key, preset) in new[] { ("Windows.Social", 0), ("Windows.Duty", 1), ("Windows.Housing", 2) }) {
            var button = new Button { Content = L(key), HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => { app.Workspace.ApplyPreset(preset); status.Text = L("Windows.LayoutApplied"); }; panel.Children.Add(button);
        }
        name.Header = L("Windows.LayoutName"); panel.Children.Add(name);
        var save = new Button { Content = L("Windows.SaveLayout") }; save.Click += async (_, _) => await SaveAsync(); panel.Children.Add(save);
        panel.Children.Add(saved);
        var restore = new Button { Content = L("Windows.LoadLayout") };
        restore.Click += async (_, _) => {
            if (saved.SelectedItem is not string id) return;
            await app.Workspace.LoadNamedAsync(id);
            if (!closed) status.Text = app.Workspace.Error ?? L("Windows.LayoutApplied");
        }; panel.Children.Add(restore); panel.Children.Add(status);
        var surface = ThemeHelper.CreateSurface(); surface.Children.Add(new ScrollViewer { Content = panel });
        Content = surface; ThemeHelper.InitializeWindow(this);
        Title = L("Windows.Layouts"); AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 720));
        Closed += (_, _) => closed = true; panel.Loaded += async (_, _) => await RefreshAsync();
    }
    private async Task RefreshAsync() {
        try { if (app.Session.Store is { } store) { var names = await store.GetLayoutNamesAsync(); if (!closed) saved.ItemsSource = names; } else status.Text = L("History.Unavailable"); }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
    }
    private async Task SaveAsync() {
        var id = name.Text.Trim();
        if (id.Length == 0 || id.Equals("current", StringComparison.OrdinalIgnoreCase)) { status.Text = L("Windows.InvalidName"); return; }
        if (app.Session.Store is not { } store) { status.Text = L("History.Unavailable"); return; }
        try {
            var names = await store.GetLayoutNamesAsync();
            if (names.Count >= 50 && !names.Contains(id)) { status.Text = L("Windows.Limit"); return; }
            await app.Workspace.SaveAsync(id); await RefreshAsync();
            if (!closed) status.Text = app.Workspace.Error ?? L("Windows.LayoutSaved");
        } catch (Exception ex) { if (!closed) status.Text = ex.Message; }
    }
}
