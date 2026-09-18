using System;
using Microsoft.UI.Xaml;

namespace XIVChat_Desktop;

public partial class ConfigWindow {
    private void LoadPrivacySettings() {
        PrivacyEnabled.IsOn = Config.Privacy.Enabled;
        PrivacySelf.IsChecked = Config.Privacy.HideSelf;
        PrivacyOthers.IsChecked = Config.Privacy.HideOthers;
        PrivacySelfName.Text = Config.Privacy.SelfName;
    }
    private void SavePrivacy_Click(object sender, RoutedEventArgs e) {
        var prior = Config.Privacy;
        try {
            var next = prior.Copy();
            next.Enabled = PrivacyEnabled.IsOn; next.HideSelf = PrivacySelf.IsChecked == true; next.HideOthers = PrivacyOthers.IsChecked == true;
            next.SelfName = XIVChatCommon.Presentation.PrivacySettings.NormalizeName(PrivacySelfName.Text);
            if (next.Enabled && !next.HideSelf && !next.HideOthers) {
                PrivacySaveStatus.Text = LocalizationHelper.GetString("Privacy.SelectScope"); return;
            }
            Config.Privacy = next; Config.Save();
            PrivacySaveStatus.Text = LocalizationHelper.GetString("Privacy.Saved");
        } catch (Exception) {
            Config.Privacy = prior; PrivacySaveStatus.Text = LocalizationHelper.GetString("Privacy.SaveFailed");
        }
    }
}
