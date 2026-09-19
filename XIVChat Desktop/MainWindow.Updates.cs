using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XIVChat_Desktop;

public partial class MainWindow {
    private System.Version? dismissedUpdate;
    private void UpdateReleaseBanner() {
        var release = App.Updates.Release;
        UpdateDetailsButton.Content = L("Update.Details");
        UpdateBanner.Message = L("Update.BannerHint");
        UpdateBanner.Title = release == null ? "" : string.Format(L("Update.Available"), DesktopReleaseClient.DisplayVersion(release.Version));
        UpdateBanner.IsOpen = App.Updates.HasUpdate && release?.Version != dismissedUpdate;
    }
    private void UpdateBanner_Closed(InfoBar sender, InfoBarClosedEventArgs args) {
        if (args.Reason == InfoBarCloseReason.CloseButton) dismissedUpdate = App.Updates.Release?.Version;
    }
    private void UpdateDetails_Click(object sender, RoutedEventArgs e) {
        var window = new ConfigWindow(App.Config);
        window.ShowUpdates();
        window.Activate();
    }
}
