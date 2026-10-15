using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatAi;

namespace XIVChat_Desktop;

public partial class ConfigWindow {
    private AiAssistant Ai => ((App)Application.Current).Ai;
    private static readonly TranslationProvider[] TranslatorOrder =
        [TranslationProvider.GoogleFree, TranslationProvider.Microsoft, TranslationProvider.DeepL, TranslationProvider.Baidu, TranslationProvider.Tencent, TranslationProvider.Ai];

    private void InitializeAi() {
        var options = Config.Ai;
        AiEnabled.IsChecked = options.Enabled;
        AiClaudeModel.Text = options.ClaudeModel;
        AiCompatibleUrl.Text = options.CompatibleBaseUrl;
        AiCompatibleModel.Text = options.CompatibleModel;
        MicrosoftRegion.Text = options.MicrosoftRegion;
        BaiduAppId.Text = options.BaiduAppId;
        TencentSecretId.Text = options.TencentSecretId;
        TencentRegion.Text = options.TencentRegion;
        LocalizeAi();
        LocalizationHelper.LanguageChanged += LocalizeAi;
        Closed += (_, _) => LocalizationHelper.LanguageChanged -= LocalizeAi;
        AiProvider.SelectedIndex = options.Provider == AiProviderKind.Claude ? 0 : 1;
        TranslatorProvider.SelectedIndex = Math.Max(0, Array.IndexOf(TranslatorOrder, options.Translator));
        AiLanguage.SelectedIndex = Math.Max(0, Array.IndexOf(TranslationLanguages.Codes, options.TargetLanguage) + 1);
        UpdateAiPanels();
    }

    private static string L(string key) => LocalizationHelper.GetString(key);

    private void LocalizeAi() {
        static void Fill(ComboBox box, params string[] items) {
            var selected = box.SelectedIndex;
            box.Items.Clear();
            foreach (var item in items) box.Items.Add(item);
            box.SelectedIndex = selected < 0 ? 0 : selected;
        }
        Fill(AiProvider, L("Ai.ProviderClaude"), L("Ai.ProviderCompatible"));
        Fill(TranslatorProvider, L("Ai.TranslatorGoogleFree"), L("Ai.TranslatorMicrosoft"), "DeepL", L("Ai.TranslatorBaidu"), L("Ai.TranslatorTencent"), L("Ai.TranslatorAi"));
        Fill(AiLanguage, L("Ai.TargetLanguageAuto"), "简体中文", "繁體中文", "English", "日本語", "한국어", "Deutsch", "Français");
        // Saved keys are never shown; leaving a box empty keeps the saved key.
        string Placeholder(string? saved, string empty = "Ai.KeyEmpty") => L(saved == null ? empty : "Ai.KeySaved");
        var o = Config.Ai;
        AiClaudeKey.PlaceholderText = Placeholder(o.ProtectedClaudeKey);
        AiCompatibleKey.PlaceholderText = Placeholder(o.ProtectedCompatibleKey, "Ai.KeyOptional");
        MicrosoftKey.PlaceholderText = Placeholder(o.ProtectedMicrosoftKey);
        DeepLKey.PlaceholderText = Placeholder(o.ProtectedDeepLKey);
        BaiduKey.PlaceholderText = Placeholder(o.ProtectedBaiduKey);
        TencentKey.PlaceholderText = Placeholder(o.ProtectedTencentKey);
    }

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAiPanels();
    private void TranslatorProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAiPanels();

    private TranslationProvider SelectedTranslator => TranslatorOrder[Math.Clamp(TranslatorProvider.SelectedIndex, 0, TranslatorOrder.Length - 1)];

    private void UpdateAiPanels() {
        static Visibility V(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
        var claude = AiProvider.SelectedIndex != 1;
        AiClaudePanel.Visibility = V(claude);
        AiCompatiblePanel.Visibility = V(!claude);
        var translator = SelectedTranslator;
        GoogleFreeNote.Visibility = V(translator == TranslationProvider.GoogleFree);
        MicrosoftPanel.Visibility = V(translator == TranslationProvider.Microsoft);
        DeepLPanel.Visibility = V(translator == TranslationProvider.DeepL);
        BaiduPanel.Visibility = V(translator == TranslationProvider.Baidu);
        TencentPanel.Visibility = V(translator == TranslationProvider.Tencent);
    }

    private bool SaveAi() {
        var o = Config.Ai;
        o.Enabled = AiEnabled.IsChecked == true;
        o.Provider = AiProvider.SelectedIndex == 1 ? AiProviderKind.OpenAiCompatible : AiProviderKind.Claude;
        o.ClaudeModel = string.IsNullOrWhiteSpace(AiClaudeModel.Text) ? AiSettings.DefaultClaudeModel : AiClaudeModel.Text.Trim();
        o.CompatibleBaseUrl = AiCompatibleUrl.Text.Trim();
        o.CompatibleModel = AiCompatibleModel.Text.Trim();
        o.Translator = SelectedTranslator;
        o.TargetLanguage = AiLanguage.SelectedIndex > 0 ? TranslationLanguages.Codes[AiLanguage.SelectedIndex - 1] : "";
        o.MicrosoftRegion = MicrosoftRegion.Text.Trim();
        o.BaiduAppId = BaiduAppId.Text.Trim();
        o.TencentSecretId = TencentSecretId.Text.Trim();
        o.TencentRegion = TencentRegion.Text.Trim();
        static string? Take(PasswordBox box, string? saved) {
            if (box.Password.Length == 0) return saved;
            var value = AiAssistant.Protect(box.Password); box.Password = ""; return value;
        }
        o.ProtectedClaudeKey = Take(AiClaudeKey, o.ProtectedClaudeKey);
        o.ProtectedCompatibleKey = Take(AiCompatibleKey, o.ProtectedCompatibleKey);
        o.ProtectedMicrosoftKey = Take(MicrosoftKey, o.ProtectedMicrosoftKey);
        o.ProtectedDeepLKey = Take(DeepLKey, o.ProtectedDeepLKey);
        o.ProtectedBaiduKey = Take(BaiduKey, o.ProtectedBaiduKey);
        o.ProtectedTencentKey = Take(TencentKey, o.ProtectedTencentKey);
        try {
            Config.Save();
            LocalizeAi();
            // Point out a bad address now instead of at the first translation.
            if (o.Provider == AiProviderKind.OpenAiCompatible && o.CompatibleBaseUrl.Length > 0) {
                try { OpenAiCompatibleClient.ParseAddress(o.CompatibleBaseUrl); }
                catch (AiException ex) { AiStatus.Text = AiAssistant.Describe(ex); return false; }
            }
            return true;
        } catch (Exception ex) {
            AiStatus.Text = L("Ai.SaveFailed") + " " + ex.Message;
            return false;
        }
    }

    private void AiSave_Click(object sender, RoutedEventArgs e) {
        if (SaveAi()) AiStatus.Text = L("Ai.Saved");
    }

    private async void TranslatorTest_Click(object sender, RoutedEventArgs e) {
        if (!SaveAi()) return;
        await RunTest(TranslatorTest, async () => await Ai.Translator().TranslateAsync("Ready for the raid tonight?", Ai.TargetCode));
    }

    private async void AiTest_Click(object sender, RoutedEventArgs e) {
        if (!SaveAi()) return;
        await RunTest(AiTest, async () => await Ai.ClientFactory(Ai.Settings()).CompleteAsync(AiPrompts.Translation("Ready for the raid tonight?", Ai.TargetLanguage)));
    }

    private async System.Threading.Tasks.Task RunTest(Button button, Func<System.Threading.Tasks.Task<string>> test) {
        button.IsEnabled = false;
        AiStatus.Text = L("Ai.Testing");
        try {
            AiStatus.Text = string.Format(L("Ai.TestPassed"), await test());
        } catch (AiException ex) {
            AiStatus.Text = AiAssistant.Describe(ex);
        } catch (Exception ex) {
            AiStatus.Text = L("Ai.Failed") + " " + ex.Message;
        } finally {
            button.IsEnabled = true;
        }
    }
}
