using System;
using Microsoft.UI.Xaml;

namespace XIVChat_Desktop;

public partial class ConfigWindow {
    private DesktopUpdates Updates => ((App)Application.Current).Updates;
    private bool updateControlsReady;
    private string? updateActionStatusKey;
    private void InitializeUpdates() {
        AutoUpdateCheck.IsChecked = Config.CheckForUpdatesOnStartup;
        updateControlsReady = true;
        Updates.Changed += RefreshUpdates;
        Config.Saved += RefreshUpdatePreference;
        Closed += (_, _) => { Updates.Changed -= RefreshUpdates; Config.Saved -= RefreshUpdatePreference; };
        RefreshUpdates();
    }
    public void ShowUpdates() => ConfigTabs.SelectedItem = TabUpdates;
    private void RefreshUpdatePreference() {
        updateControlsReady = false;
        AutoUpdateCheck.IsChecked = Config.CheckForUpdatesOnStartup;
        updateControlsReady = true;
    }
    private void RefreshUpdates() {
        string L(string key) => LocalizationHelper.GetString(key);
        UpdateCurrentVersion.Text = string.Format(L("Update.CurrentVersion"), Updates.CurrentVersionText);
        CheckUpdatesButton.IsEnabled = Updates.State != UpdateCheckState.Checking;
        UpdateStatus.Text = L(Updates.State switch {
            UpdateCheckState.Checking => "Update.Checking",
            UpdateCheckState.Current => "Update.Current",
            UpdateCheckState.Failed => "Update.Failed",
            UpdateCheckState.Available => "Update.Found",
            _ => "Update.Idle"
        });
        UpdateActionStatus.Text = updateActionStatusKey == null ? "" : L(updateActionStatusKey);
        UpdateAvailablePanel.Visibility = Updates.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        if (Updates.Release is { } release) {
            UpdateAvailableVersion.Text = string.Format(L("Update.Available"), DesktopReleaseClient.DisplayVersion(release.Version));
            UpdateReleaseNotes.Text = string.IsNullOrWhiteSpace(release.Notes) ? L("Update.NoNotes") : release.Notes;
        }
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) {
        updateActionStatusKey = null;
        await Updates.CheckAsync();
    }
    private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e) {
        if (!updateControlsReady) return;
        var old = Config.CheckForUpdatesOnStartup;
        try {
            Config.CheckForUpdatesOnStartup = AutoUpdateCheck.IsChecked == true;
            Config.Save();
            updateActionStatusKey = null;
        } catch {
            Config.CheckForUpdatesOnStartup = old;
            RefreshUpdatePreference();
            updateActionStatusKey = "Update.SaveFailed";
        }
        RefreshUpdates();
    }
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) {
        if (Updates.HasUpdate) await OpenUpdateLinkAsync(Updates.Release!.Download);
    }
    private async void ReleasePage_Click(object sender, RoutedEventArgs e) {
        if (Updates.HasUpdate) await OpenUpdateLinkAsync(Updates.Release!.Page);
    }
    private async System.Threading.Tasks.Task OpenUpdateLinkAsync(Uri url) {
        try {
            updateActionStatusKey = await Windows.System.Launcher.LaunchUriAsync(url) ? null : "Update.OpenFailed";
        } catch { updateActionStatusKey = "Update.OpenFailed"; }
        RefreshUpdates();
    }
}
