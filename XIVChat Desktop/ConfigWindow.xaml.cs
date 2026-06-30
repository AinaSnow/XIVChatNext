using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace XIVChat_Desktop {
    public partial class ConfigWindow : Window {
        public Configuration Config { get; private set; }

        public ConfigWindow(Configuration config) {
            this.Config = config;

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(650, 520));

            this.ThemeChooser.SelectionChanged -= ThemeChooser_SelectionChanged;
            this.ThemeChooser.ItemsSource = (Theme[])Enum.GetValues(typeof(Theme));
            this.ThemeChooser.SelectedItem = this.Config.Theme;
            this.ThemeChooser.SelectionChanged += ThemeChooser_SelectionChanged;
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
            if (this.ThemeChooser.SelectedItem is Theme theme && this.Config.Theme != theme) {
                this.Config.Theme = theme;
                App.ApplyTheme(theme);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            this.Config.Save();
        }

        private void SavedServers_ItemDoubleClick(SavedServer? server) {
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
