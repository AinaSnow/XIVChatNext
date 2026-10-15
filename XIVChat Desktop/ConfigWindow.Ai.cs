using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatAi;

namespace XIVChat_Desktop;

public partial class ConfigWindow {
    private AiAssistant Ai => ((App)Application.Current).Ai;

    private void InitializeAi() {
        var options = Config.Ai;
        AiEnabled.IsChecked = options.Enabled;
        AiClaudeModel.Text = options.ClaudeModel;
        AiCompatibleUrl.Text = options.CompatibleBaseUrl;
        AiCompatibleModel.Text = options.CompatibleModel;
        AiLanguage.Text = options.TargetLanguage;
        LocalizeAi();
        LocalizationHelper.LanguageChanged += LocalizeAi;
        Closed += (_, _) => LocalizationHelper.LanguageChanged -= LocalizeAi;
        AiProvider.SelectedIndex = options.Provider == AiProviderKind.Claude ? 0 : 1;
        UpdateAiPanels();
    }

    private void LocalizeAi() {
        var selected = AiProvider.SelectedIndex;
        AiProvider.Items.Clear();
        AiProvider.Items.Add(LocalizationHelper.GetString("Ai.ProviderClaude"));
        AiProvider.Items.Add(LocalizationHelper.GetString("Ai.ProviderCompatible"));
        AiProvider.SelectedIndex = selected < 0 ? 0 : selected;
        // Saved keys are never shown; leaving the box empty keeps them.
        AiClaudeKey.PlaceholderText = LocalizationHelper.GetString(Config.Ai.ProtectedClaudeKey == null ? "Ai.KeyEmpty" : "Ai.KeySaved");
        AiCompatibleKey.PlaceholderText = LocalizationHelper.GetString(Config.Ai.ProtectedCompatibleKey == null ? "Ai.KeyOptional" : "Ai.KeySaved");
        AiLanguage.PlaceholderText = LocalizationHelper.GetString("Ai.TargetLanguageAuto");
    }

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAiPanels();

    private void UpdateAiPanels() {
        var claude = AiProvider.SelectedIndex != 1;
        AiClaudePanel.Visibility = claude ? Visibility.Visible : Visibility.Collapsed;
        AiCompatiblePanel.Visibility = claude ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool SaveAi() {
        var options = Config.Ai;
        options.Enabled = AiEnabled.IsChecked == true;
        options.Provider = AiProvider.SelectedIndex == 1 ? AiProviderKind.OpenAiCompatible : AiProviderKind.Claude;
        options.ClaudeModel = string.IsNullOrWhiteSpace(AiClaudeModel.Text) ? AiSettings.DefaultClaudeModel : AiClaudeModel.Text.Trim();
        options.CompatibleBaseUrl = AiCompatibleUrl.Text.Trim();
        options.CompatibleModel = AiCompatibleModel.Text.Trim();
        options.TargetLanguage = AiLanguage.Text.Trim();
        if (AiClaudeKey.Password.Length > 0) { options.ProtectedClaudeKey = AiAssistant.Protect(AiClaudeKey.Password); AiClaudeKey.Password = ""; }
        if (AiCompatibleKey.Password.Length > 0) { options.ProtectedCompatibleKey = AiAssistant.Protect(AiCompatibleKey.Password); AiCompatibleKey.Password = ""; }
        try {
            Config.Save();
            LocalizeAi();
            return true;
        } catch (Exception ex) {
            AiStatus.Text = LocalizationHelper.GetString("Ai.SaveFailed") + " " + ex.Message;
            return false;
        }
    }

    private void AiSave_Click(object sender, RoutedEventArgs e) {
        if (SaveAi()) AiStatus.Text = LocalizationHelper.GetString("Ai.Saved");
    }

    private async void AiTest_Click(object sender, RoutedEventArgs e) {
        if (!SaveAi()) return;
        AiTest.IsEnabled = false;
        AiStatus.Text = LocalizationHelper.GetString("Ai.Testing");
        try {
            var reply = await Ai.ClientFactory(Ai.Settings()).CompleteAsync(AiPrompts.Translation("Ready for the raid tonight?", Ai.TargetLanguage));
            AiStatus.Text = string.Format(LocalizationHelper.GetString("Ai.TestPassed"), reply);
        } catch (AiException ex) {
            AiStatus.Text = AiAssistant.Describe(ex);
        } catch (Exception ex) {
            AiStatus.Text = LocalizationHelper.GetString("Ai.Failed") + " " + ex.Message;
        } finally {
            AiTest.IsEnabled = true;
        }
    }
}
