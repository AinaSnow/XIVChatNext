using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public partial class Export : Window, INotifyPropertyChanged {
        public App App => (App)Application.Current;

        public Tab ExportTab { get; }

        public ExportFilter Filter => (ExportFilter)this.ExportTab.Filter;

        public ObservableCollection<ServerMessage.SenderPlayer> Senders { get; } = new ObservableCollection<ServerMessage.SenderPlayer>();
        public ObservableCollection<ServerMessage.SenderPlayer> SenderFilters => this.Filter.Senders;

        private bool showTimestamps = true;

        public bool ShowTimestamps {
            get => this.showTimestamps;
            set {
                this.showTimestamps = value;
                this.OnPropertyChanged(nameof(this.ShowTimestamps));
            }
        }

        public Export() {
            this.ExportTab = new Tab("Export") {
                Filter = new ExportFilter {
                    Types = Tab.GeneralFilter().Types,
                },
            };

            this.Repopulate();

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);

            this.SetUpFilters();
        }

        private void SetUpFilters() {
            foreach (var category in (FilterCategory[])Enum.GetValues(typeof(FilterCategory))) {
                var tabContent = new StackPanel {
                    Margin = new Thickness(8),
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

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

                var doingMultiple = false;

                void SetAllChecked(bool isChecked) {
                    doingMultiple = true;

                    foreach (var child in tabContent.Children) {
                        if (!(child is CheckBox)) {
                            continue;
                        }

                        var check = (CheckBox)child;
                        check.IsChecked = isChecked;
                    }

                    this.Repopulate();

                    doingMultiple = false;
                }

                buttonsPanel.Children.Add(selectButton);
                buttonsPanel.Children.Add(deselectButton);

                tabContent.Children.Add(buttonsPanel);

                foreach (var type in category.Types()) {
                    var check = new CheckBox {
                        Content = type.Name(),
                        IsChecked = this.ExportTab.Filter.Types.Contains(type),
                    };

                    check.Checked += (sender, e) => {
                        this.ExportTab.Filter.Types.Add(type);

                        if (!doingMultiple) {
                            this.Repopulate();
                        }
                    };
                    check.Unchecked += (sender, e) => {
                        this.ExportTab.Filter.Types.Remove(type);

                        if (!doingMultiple) {
                            this.Repopulate();
                        }
                    };

                    tabContent.Children.Add(check);
                }

                var tabItem = new TabViewItem {
                    Header = new TextBlock { Text = category.Name() },
                    Content = tabContent,
                };

                this.Tabs.TabItems.Add(tabItem);
            }
        }

        private void Repopulate() {
            this.ExportTab.RepopulateMessages(this.App.Window.Messages);
            this.SetUpSenders();
        }

        private void SetUpSenders() {
            var senders = this.App.Window.Messages
                .Where(msg => ((ExportFilter)this.ExportTab.Filter).AllowedMinusSenders(msg))
                .Select(msg => msg.GetSenderPlayer())
                .Where(sender => sender != null)
                .Distinct()
                .ToList();

            this.Senders.Clear();

            foreach (var sender in senders) {
                this.Senders.Add(sender!);
            }
        }

        public class ExportFilter : Filter {
            public ObservableCollection<ServerMessage.SenderPlayer> Senders { get; } = new ObservableCollection<ServerMessage.SenderPlayer>();

            public DateTime? Before { get; set; }
            public DateTime? After { get; set; }

            public void AddSender(ServerMessage.SenderPlayer sender) {
                if (this.Senders.Contains(sender)) {
                    return;
                }

                this.Senders.Add(sender);
            }

            public bool AllowedMinusSenders(ServerMessage message) {
                if (!base.Allowed(message)) {
                    return false;
                }

                if (this.Before != null && message.Timestamp > this.Before) {
                    return false;
                }

                if (this.After != null && message.Timestamp < this.After) {
                    return false;
                }

                return true;
            }

            public override bool Allowed(ServerMessage message) {
                if (!this.AllowedMinusSenders(message)) {
                    return false;
                }

                // check sender if any senders are selected
                var sender = message.GetSenderPlayer();
                if (this.Senders.Count != 0 && sender != null && !this.Senders.Contains(sender)) {
                    return false;
                }

                // our stuff
                return true;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged1([CallerMemberName] string? propertyName = null) {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void Markdown_Checked(object sender, RoutedEventArgs e) => this.SetMarkdownProcessing(true);

        private void Markdown_Unchecked(object sender, RoutedEventArgs e) => this.SetMarkdownProcessing(false);

        private void SetMarkdownProcessing(bool on) {
            this.ExportTab.ProcessMarkdown = on;
        }

        private void RightArrow_Click(object sender, RoutedEventArgs e) {
            var idx = this.SendersFilterSource.SelectedIndex;
            if (idx == -1) {
                return;
            }

            var player = this.Senders[idx];

            var filter = (ExportFilter)this.ExportTab.Filter;
            filter.AddSender(player);

            this.Repopulate();
        }

        private void LeftArrow_Click(object sender, RoutedEventArgs e) {
            var idx = this.SenderFiltersDest.SelectedIndex;
            if (idx == -1) {
                return;
            }

            var filter = (ExportFilter)this.ExportTab.Filter;

            var player = filter.Senders.ElementAt(idx);

            filter.Senders.Remove(player);

            this.Repopulate();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void Save_Click(object sender, RoutedEventArgs e) {
            // ask the user where to save
            var savePicker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Text Files", new[] { ".txt" });
            savePicker.FileTypeChoices.Add("Rich Text Files", new[] { ".rtf" });
            savePicker.SuggestedFileName = "XIVChat Export";

            var file = await savePicker.PickSaveFileAsync();
            if (file == null) {
                return;
            }

            // build export text
            var text = new System.Text.StringBuilder();
            foreach (var message in this.ExportTab.Messages) {
                foreach (var chunk in message.Chunks) {
                    if (chunk is TextChunk textChunk) {
                        text.Append(textChunk.Content);
                    }
                }
                text.AppendLine();
            }

            await FileIO.WriteTextAsync(file, text.ToString());

            // show completion box
            // TODO: Show success dialog
        }

        private void AfterDatePicker_OnDateChanged(object sender, DatePickerValueChangedEventArgs e) {
            this.Filter.After = e.NewDate.DateTime;
            this.Repopulate();
        }

        private void AfterTimePicker_OnTimeChanged(object sender, TimePickerValueChangedEventArgs e) {
            // TODO: Update time
        }

        private void BeforeDatePicker_OnDateChanged(object sender, DatePickerValueChangedEventArgs e) {
            this.Filter.Before = e.NewDate.DateTime;
            this.Repopulate();
        }

        private void BeforeTimePicker_OnTimeChanged(object sender, TimePickerValueChangedEventArgs e) {
            // TODO: Update time
        }

        private void BeforeClear_Click(object sender, RoutedEventArgs e) {
            this.BeforeDatePicker.SelectedDate = null;
            this.Filter.Before = null;
            this.Repopulate();
        }

        private void AfterClear_Click(object sender, RoutedEventArgs e) {
            this.AfterDatePicker.SelectedDate = null;
            this.Filter.After = null;
            this.Repopulate();
        }
    }
}
