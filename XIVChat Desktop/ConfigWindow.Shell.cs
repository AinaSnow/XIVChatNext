using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace XIVChat_Desktop;

public partial class ConfigWindow {
    private ShellIntegration Shell => ((App)Application.Current).Shell;
    private bool shellControlsReady;
    private void InitializeShell() {
        Shell.Changed += RefreshShell;
        LocalizationHelper.LanguageChanged += RefreshShell;
        Closed += (_, _) => { Shell.Changed -= RefreshShell; LocalizationHelper.LanguageChanged -= RefreshShell; };
        RefreshShell();
        shellControlsReady = true;
    }
    private void RefreshShell() {
        shellControlsReady = false;
        ChkCloseToTray.IsChecked = Config.CloseToTray;
        ChkHotkey.IsChecked = Config.GlobalHotkeyEnabled;
        HotkeyBox.Text = ShellIntegration.FormatShortcut(Config.GlobalHotkeyModifiers, Config.GlobalHotkeyKey);
        HotkeyBox.IsEnabled = Config.GlobalHotkeyEnabled;
        HotkeyStatus.Text = Config.GlobalHotkeyEnabled && Shell.HotkeyFailed ? LocalizationHelper.GetString("Config.HotkeyInUse") : "";
        HotkeyStatus.Visibility = HotkeyStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        shellControlsReady = true;
    }
    private void ApplyShell(Action change) {
        change();
        try { Config.Save(); } catch (Exception) { /* Applied for this session; the Save button reports persistent failures. */ }
        Shell.Apply();
    }
    private void CloseToTray_Click(object sender, RoutedEventArgs e) {
        if (shellControlsReady) ApplyShell(() => Config.CloseToTray = ChkCloseToTray.IsChecked == true);
    }
    private void Hotkey_Click(object sender, RoutedEventArgs e) {
        if (shellControlsReady) ApplyShell(() => Config.GlobalHotkeyEnabled = ChkHotkey.IsChecked == true);
    }
    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e) {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        uint modifiers = 0;
        if (Down(VirtualKey.Control)) modifiers |= ShellIntegration.ModControl;
        if (Down(VirtualKey.Menu)) modifiers |= ShellIntegration.ModAlt;
        if (Down(VirtualKey.Shift)) modifiers |= ShellIntegration.ModShift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= ShellIntegration.ModWin;
        // Let plain Tab move focus so the field stays keyboard accessible.
        if (e.Key == VirtualKey.Tab && modifiers == 0) return;
        e.Handled = true;
        if (!ShellIntegration.IsValidShortcut(modifiers, (uint)e.Key)) return;
        ApplyShell(() => { Config.GlobalHotkeyModifiers = modifiers; Config.GlobalHotkeyKey = (uint)e.Key; });
    }
}
