using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace XIVChat_Desktop {
    public partial class ConfigWindow : Window {
        public Configuration Config { get; private set; }
        private void InitialSetup_Click(object sender, RoutedEventArgs e) => SetupWizard.Show();

        public ConfigWindow(Configuration config) {
            this.Config = config;

            this.InitializeComponent();
            InitializeUpdates();
            LoadPrivacySettings();
            ((App)Application.Current).Presentation.PolicyChanged += LoadPrivacySettings;
            this.Closed += (_, _) => ((App)Application.Current).Presentation.PolicyChanged -= LoadPrivacySettings;
            this.HistoryRetention.Value = this.Config.HistoryRetentionDays;
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(650, 520));

            this.ThemeChooser.SelectionChanged -= ThemeChooser_SelectionChanged;
            this.ThemeChooser.ItemsSource = Enum.GetValues<Theme>().Select(t => LocalizationHelper.GetString("Theme." + t)).ToList();
            this.ThemeChooser.SelectedIndex = Array.IndexOf(Enum.GetValues<Theme>(), this.Config.Theme);
            this.ThemeChooser.SelectionChanged += ThemeChooser_SelectionChanged;

            this.LanguageChooser.SelectionChanged -= LanguageChooser_SelectionChanged;
            this.LanguageChooser.ItemsSource = LocalizationHelper.AvailableLanguages.Select(LocalizationHelper.GetLanguageName).ToList();
            this.LanguageChooser.SelectedIndex = Array.IndexOf(LocalizationHelper.AvailableLanguages, this.Config.Language);
            this.LanguageChooser.SelectionChanged += LanguageChooser_SelectionChanged;

            Localize.BindWindow(this, UpdateLocalizations);
            ((App)Application.Current).Notifier.StatusChanged += UpdateNotificationStatus;
            this.Closed += (_, _) => ((App)Application.Current).Notifier.StatusChanged -= UpdateNotificationStatus;
            UpdateNotificationStatus();
        }

        public void UpdateLocalizations() {
            RefreshUpdates();
            this.ThemeChooser.SelectionChanged -= ThemeChooser_SelectionChanged;
            this.ThemeChooser.ItemsSource = Enum.GetValues<Theme>().Select(t => LocalizationHelper.GetString("Theme." + t)).ToList();
            this.ThemeChooser.SelectedIndex = Array.IndexOf(Enum.GetValues<Theme>(), this.Config.Theme);
            this.ThemeChooser.SelectionChanged += ThemeChooser_SelectionChanged;
            try {
                this.Title = LocalizationHelper.GetString("Menu.Config");
                TabServers.Header = LocalizationHelper.GetString("Config.Servers");
                TabPrivacy.Header = LocalizationHelper.GetString("Privacy.Title");
                PrivacyEnabled.Header = LocalizationHelper.GetString("Privacy.Title");
                PrivacyEnabled.OnContent = LocalizationHelper.GetString("Privacy.Active");
                PrivacyEnabled.OffContent = LocalizationHelper.GetString("Privacy.Off");
                PrivacySelf.Content = LocalizationHelper.GetString("Privacy.HideSelf");
                PrivacyOthers.Content = LocalizationHelper.GetString("Privacy.HideOthers");
                PrivacySelfName.Header = LocalizationHelper.GetString("Privacy.SelfName");
                PrivacySelfName.PlaceholderText = LocalizationHelper.GetString("Privacy.Me");
                PrivacyHelp.Text = LocalizationHelper.GetString("Privacy.Help");
                PrivacyLimits.Text = LocalizationHelper.GetString("Privacy.Limits");
                SavePrivacy.Content = LocalizationHelper.GetString("Dialog.Save");
                TabWindow.Header = LocalizationHelper.GetString("Config.Window");
                TabConnection.Header = LocalizationHelper.GetString("Config.Connection");
                TabNotifications.Header = LocalizationHelper.GetString("Config.Notifications");
                TabHistory.Header = LocalizationHelper.GetString("History.Title");
                ChkHistoryEnabled.Content = LocalizationHelper.GetString("History.Enabled");
                HistoryRetention.Header = LocalizationHelper.GetString("History.Retention");
                HistoryRetentionHelp.Text = LocalizationHelper.GetString("History.RetentionHelp");
                BtnSaveHistory.Content = LocalizationHelper.GetString("Dialog.Save");
                ChkOnlineAvatars.Content = LocalizationHelper.GetString("Avatar.Online");

                ChkAlwaysOnTop.Content = LocalizationHelper.GetString("Config.AlwaysOnTop");
                ChkCompactMode.Content = LocalizationHelper.GetString("Config.CompactMode");
                ThemeChooser.Header = LocalizationHelper.GetString("Config.Theme");
                LanguageChooser.Header = LocalizationHelper.GetString("Config.Language");
                SliderOpacity.Header = LocalizationHelper.GetString("Config.Opacity");
                TxtFontSize.Header = LocalizationHelper.GetString("Config.FontSize");
                TxtLocalBacklog.Header = LocalizationHelper.GetString("Config.LocalBacklogMessages");
                TxtBacklog.Header = LocalizationHelper.GetString("Config.BacklogMessages");

                BtnSaveWindow.Content = LocalizationHelper.GetString("Dialog.Save");
                BtnSaveConnection.Content = LocalizationHelper.GetString("Dialog.Save");
                BtnAddNotification.Content = LocalizationHelper.GetString("Dialog.Add");
                BtnEditNotification.Content = LocalizationHelper.GetString("Dialog.Edit");
                BtnDeleteNotification.Content = LocalizationHelper.GetString("Dialog.Delete");
            } catch { }
        }

        private void AlwaysOnTop_Checked(object sender, RoutedEventArgs e) {
            this.SetAlwaysOnTop(true);
        }

        private void AlwaysOnTop_Unchecked(object sender, RoutedEventArgs e) {
            this.SetAlwaysOnTop(false);
        }

        private void SetAlwaysOnTop(bool onTop) {
            this.Config.AlwaysOnTop = onTop;
            App.ApplyAlwaysOnTop(onTop);
        }

        private void ThemeChooser_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (this.ThemeChooser.SelectedIndex is var index && index >= 0 && index < Enum.GetValues<Theme>().Length) {
                var theme = Enum.GetValues<Theme>()[index];
                this.Config.Theme = theme;
                App.ApplyTheme(theme);
            }
        }

        private void LanguageChooser_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            int idx = this.LanguageChooser.SelectedIndex;
            if (idx >= 0 && idx < LocalizationHelper.AvailableLanguages.Length) {
                var lang = LocalizationHelper.AvailableLanguages[idx];
                if (this.Config.Language != lang) {
                    this.Config.Language = lang;
                    LocalizationHelper.ApplyLanguage(lang);
                }
            }
        }

        private void HistoryRetention_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) {
            if (!double.IsNaN(args.NewValue)) this.Config.HistoryRetentionDays = (int)Math.Clamp(args.NewValue, 0, 36500);
        }

        private async void Save_Click(object sender, RoutedEventArgs e) {
            this.Config.Save();
            var session = ((App)Application.Current).Session;
            if (session.Store != null && session.StorageError == null) {
                try { await session.Store.PruneAsync(this.Config.HistoryRetentionDays, DateTime.UtcNow); }
                catch (Exception ex) { session.ReportStorageError(ex); }
            }
        }

        private void SavedServers_ItemDoubleClick(SavedServer? server) {
            if (server?.Relay != null) { new RelayPairDialog(server).Activate(); return; }
            var dialog = new ManageServer(server);
            dialog.Activate();
        }

        private void NumericInputFilter(object sender, RoutedEventArgs e) {
            if (sender is TextBox textBox && textBox.Text != null) {
                var allDigits = textBox.Text.All(char.IsDigit);
                if (!allDigits) {
                    textBox.Text = new string(textBox.Text.Where(char.IsDigit).ToArray());
                }
            }
        }

        private void FontSize_TextChanged(object sender, TextChangedEventArgs e) {
            if (sender is TextBox textBox && double.TryParse(textBox.Text, out var val) && val > 0) {
                this.Config.FontSize = val;
            }
        }

        private void LocalBacklog_TextChanged(object sender, TextChangedEventArgs e) {
            if (sender is TextBox textBox) {
                NumericInputFilter(sender, null!);
                if (uint.TryParse(textBox.Text, out var val)) {
                    this.Config.LocalBacklogMessages = val;
                }
            }
        }

        private void Backlog_TextChanged(object sender, TextChangedEventArgs e) {
            if (sender is TextBox textBox) {
                NumericInputFilter(sender, null!);
                if (ushort.TryParse(textBox.Text, out var val)) {
                    this.Config.BacklogMessages = val;
                }
            }
        }

        private void Notifications_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) {
            var context = ((FrameworkElement)e.OriginalSource).DataContext;
            if (!(context is Notification notification)) {
                return;
            }

            var dialog = new ManageNotification(notification);
            dialog.Activate();
        }

        private void UpdateNotificationStatus() {
            NotificationPlatformStatus.Text = ((App)Application.Current).Notifier.PlatformError == null ? "" : LocalizationHelper.GetString("Notify.SystemUnavailable");
        }
        private async void NotificationTest_Click(object sender, RoutedEventArgs e) {
            await ((App)Application.Current).Notifier.TestAsync();
            NotificationPlatformStatus.Text = LocalizationHelper.GetString(this.Config.NotificationOptions.IsQuiet(DateTime.Now) ? "Notify.TestQuiet" :
                ((App)Application.Current).Notifier.PlatformError != null ? "Notify.SystemUnavailable" : "Notify.TestSent");
        }

        private void Notifications_Add_Click(object sender, RoutedEventArgs e) {
            var dialog = new ManageNotification(null);
            dialog.Activate();
        }

        private void Notifications_Edit_Click(object sender, RoutedEventArgs e) {
            if (!(this.Notifications.SelectedItem is Notification notif)) {
                return;
            }

            var dialog = new ManageNotification(notif);
            dialog.Activate();
        }

        private void Notifications_Delete_Click(object sender, RoutedEventArgs e) {
            if (!(this.Notifications.SelectedItem is Notification notif)) {
                return;
            }

            this.Config.Notifications.Remove(notif);
            this.Config.Save();
        }
    }
}
