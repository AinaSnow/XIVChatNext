using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    public partial class ManageNotification : Window {
        public App App => (App)Application.Current;

        public Notification Notification { get; }

        private bool NewNotification { get; }

        public ObservableCollection<StringWrapper> Regexes { get; }
        public ObservableCollection<StringWrapper> Substrings { get; }

        public ManageNotification(Notification? notification) {
            this.NewNotification = notification == null;
            this.Notification = notification ?? new Notification("");
            this.Regexes = new ObservableCollection<StringWrapper>(this.Notification.Regexes.Select(regex => new StringWrapper(regex)));
            this.Substrings = new ObservableCollection<StringWrapper>(this.Notification.Substrings.Select(sub => new StringWrapper(sub)));

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(650, 500));

            this.SetUpChannels();
        }

        private void SetUpChannels() {
            var buttonsPanel = new StackPanel {
                Margin = new Thickness(0, 0, 0, 4),
                Orientation = Orientation.Horizontal,
            };

            var selectButton = new Button {
                Content = "全选",
            };
            selectButton.Click += (sender, e) => SetAllChecked(true);

            var deselectButton = new Button {
                Content = "取消全选",
                Margin = new Thickness(4, 0, 0, 0),
            };
            deselectButton.Click += (sender, e) => SetAllChecked(false);

            void SetAllChecked(bool isChecked) {
                foreach (var child in this.Channels.Children) {
                    if (!(child is CheckBox)) {
                        continue;
                    }

                    var check = (CheckBox)child;
                    check.IsChecked = isChecked;
                }
            }

            buttonsPanel.Children.Add(selectButton);
            buttonsPanel.Children.Add(deselectButton);

            this.Channels.Children.Add(buttonsPanel);

            foreach (var type in (ChatType[])Enum.GetValues(typeof(ChatType))) {
                var check = new CheckBox {
                    Content = type.Name(),
                    IsChecked = this.Notification.Channels.Contains(type),
                };

                check.Checked += (sender, e) => {
                    this.Notification.Channels.Add(type);
                };
                check.Unchecked += (sender, e) => {
                    this.Notification.Channels.Remove(type);
                };

                this.Channels.Children.Add(check);
            }
        }

        private void AddRegex_Click(object sender, RoutedEventArgs e) {
            this.Regexes.Add(new StringWrapper(string.Empty));
        }

        private void AddSubstring_Click(object sender, RoutedEventArgs e) {
            this.Substrings.Add(new StringWrapper(string.Empty));
        }

        private void ManageNotification_OnClosed(object sender, WindowEventArgs args) {
            this.Notification.Regexes = this.Regexes
                .Select(wrapper => wrapper.Value)
                .Where(regex => regex.Length > 0 && regex.IsValidRegex())
                .ToList();

            this.Notification.Substrings = this.Substrings
                .Select(wrapper => wrapper.Value)
                .Where(substring => substring.Length > 0)
                .ToList();

            if (this.NewNotification) {
                this.App.Config.Notifications.Add(this.Notification);
            }

            this.App.Config.Save();
        }
    }
}
