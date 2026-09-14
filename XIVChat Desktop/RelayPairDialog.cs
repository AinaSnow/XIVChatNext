using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;

namespace XIVChat_Desktop;

public sealed class RelayPairDialog : Window {
    private readonly App app = (App)Application.Current;
    private readonly CancellationTokenSource life = new();
    private readonly TextBox name = new();
    private readonly PasswordBox invitation = new() { MaxLength = 8192 };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox verified = new();
    private readonly Button inspect = new(), save = new();
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly SavedServer? original;
    private Invitation? parsed;
    private string? inspectedInvitation;
    private RelayProfile? paired;
    private bool busy;
    public RelayPairDialog(SavedServer? original = null) {
        this.original = original; name.Text = original?.Name ?? "";
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        foreach (var control in new UIElement[] { hint, name, invitation, inspect, details, verified, error, save }) panel.Children.Add(control);
        var surface = ThemeHelper.CreateSurface();
        surface.Children.Add(new ScrollViewer { Content = panel });
        Content = surface;
        ThemeHelper.InitializeWindow(this); AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 640));
        Localize.BindWindow(this, LocalizeWindow);
        invitation.PasswordChanged += (_, _) => {
            if (inspectedInvitation == invitation.Password) return;
            parsed = null; inspectedInvitation = null; verified.IsChecked = false; details.Text = "";
        };
        inspect.Click += (_, _) => {
            try { parsed = RelayEndpoint.DecodeInvitation(invitation.Password); inspectedInvitation = invitation.Password; verified.IsChecked = false; details.Text = parsed.Server + "\n\n" + parsed.Fingerprint; error.Text = ""; }
            catch (Exception) { parsed = null; inspectedInvitation = null; error.Text = SetupText.T("邀请格式无效或已过期，请从游戏插件重新复制邀请。", "The invitation is invalid or expired. Copy a new invitation from the game plugin."); }
        };
        save.Click += Save;
        Closed += (_, _) => life.Cancel();
    }
    private void LocalizeWindow() {
        Title = SetupText.T("自建中继连接", "Self-hosted relay connection");
        hint.Text = SetupText.T("在游戏插件中生成邀请并粘贴到这里。核对插件显示的完整指纹，再完成配对。已有连接可直接修改名称；重新配对需生成新邀请。", "Paste an invitation from the game plugin. Compare its full fingerprint with the plugin before pairing. An existing connection can be renamed without a new invitation.");
        name.Header = SetupText.T("连接名称", "Connection name"); invitation.Header = SetupText.T("一次性邀请", "One-time invitation");
        inspect.Content = SetupText.T("查看地址和指纹", "Inspect address and fingerprint");
        verified.Content = SetupText.T("已与游戏插件核对完整指纹", "I compared the full fingerprint with the game plugin");
        save.Content = SetupText.T("保存连接", "Save connection");
    }
    private async void Save(object sender, RoutedEventArgs args) {
        if (busy) return;
        if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = SetupText.T("请输入连接名称。", "Enter a connection name."); return; }
        if (paired == null && !(original?.Relay != null && invitation.Password.Length == 0) && (parsed == null || inspectedInvitation != invitation.Password || verified.IsChecked != true)) {
            error.Text = SetupText.T("请先查看邀请并核对指纹。", "Inspect the invitation and confirm the fingerprint first."); return;
        }
        busy = true; save.IsEnabled = inspect.IsEnabled = invitation.IsEnabled = name.IsEnabled = false;
        SavedServer? added = null;
        var oldName = original?.Name; var oldRelay = original?.Relay; var oldLast = app.Config.LastSuccessfulConnection;
        try {
            if (paired == null && parsed != null) {
                var result = await RelayEndpoint.PairAsync(parsed, name.Text.Trim(), life.Token);
                // Keep a successfully redeemed credential in memory if local persistence fails, so Save can retry without consuming another invitation.
                paired = new RelayProfile(parsed.Server, result.DeviceId, result.ClientId, WindowsSecret.Protect(result.Credential), result.Fingerprint);
            }
            life.Token.ThrowIfCancellationRequested();
            var target = original ?? new SavedServer(name.Text.Trim(), "", 0);
            target.Name = name.Text.Trim(); target.Relay = paired ?? original!.Relay;
            if (original == null) { added = target; app.Config.Servers.Add(target); }
            if (app.Config.LastSuccessfulConnection?.Id == target.Id) app.Config.LastSuccessfulConnection = null;
            app.Config.Save(); Close();
        } catch (Exception ex) {
            if (added != null) app.Config.Servers.Remove(added);
            if (original != null) { original.Name = oldName!; original.Relay = oldRelay; }
            app.Config.LastSuccessfulConnection = oldLast;
            if (!life.IsCancellationRequested) error.Text = ex.Message;
        } finally {
            busy = false; save.IsEnabled = name.IsEnabled = true;
            inspect.IsEnabled = invitation.IsEnabled = paired == null;
        }
    }
}
