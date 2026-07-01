using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public partial class MainWindow : INotifyPropertyChanged {
        public App App => (App)Application.Current;

        public List<ServerMessage> Messages { get; } = new List<ServerMessage>();

        public Microsoft.UI.Xaml.Controls.TextBlock LoggedInAsText => this.LoggedInAs;
        public Microsoft.UI.Xaml.Controls.TextBlock LoggedInAsSeparatorText => this.LoggedInAsSeparator;
        public Microsoft.UI.Xaml.Controls.TextBlock CurrentWorldText => this.CurrentWorld;
        public Microsoft.UI.Xaml.Controls.TextBlock CurrentWorldSeparatorText => this.CurrentWorldSeparator;
        public Microsoft.UI.Xaml.Controls.TextBlock LocationText => this.Location;
        public Microsoft.UI.Xaml.Controls.HyperlinkButton LocationButton => this.LocationBtn;
        public XIVChatCommon.Message.Server.PlayerData? CurrentPlayerData { get; set; }

        private int historyIndex = -1;

        private int HistoryIndex {
            get => this.historyIndex;
            set {
                var idx = Math.Min(this.History.Count - 1, Math.Max(-1, value));
                this.historyIndex = idx;
            }
        }

        private int ReverseHistoryIndex => this.HistoryIndex == -1 ? -1 : Math.Max(-1, this.History.Count - this.HistoryIndex - 1);

        private string? HistoryBuffer { get; set; }

        private List<string> History { get; } = new List<string>();

        public string InputPlaceholder => this.App.Connection?.Available == true ? "Typing words…" : LocalizationHelper.GetString("Status.Disconnected");

        public MainWindow() {
            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(850, 600));
            this.Title = LocalizationHelper.GetString("AppTitle");
            this.PopulateTabs();
            UpdateLocalizations();
        }

        public void UpdateLocalizations() {
            try {
                this.Title = LocalizationHelper.GetString("AppTitle");
                MenuMain.Title = LocalizationHelper.GetString("Menu.XIVChat");
                MenuConnect.Text = LocalizationHelper.GetString("Menu.Connect");
                MenuDisconnect.Text = LocalizationHelper.GetString("Menu.Disconnect");
                MenuExport.Text = LocalizationHelper.GetString("Menu.Export");
                MenuConfig.Text = LocalizationHelper.GetString("Menu.Config");
                MenuExit.Text = LocalizationHelper.GetString("Menu.Exit");
                OnPropertyChanged(nameof(InputPlaceholder));
            } catch { }
        }

        private void PopulateTabs() {
            this.Tabs.TabItems.Clear();
            foreach (var tab in this.App.Config.Tabs) {
                this.AddTab(tab);
            }
            this.App.Config.Tabs.CollectionChanged -= this.OnTabsCollectionChanged;
            this.App.Config.Tabs.CollectionChanged += this.OnTabsCollectionChanged;
        }

        private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            this.App.Dispatch(() => {
                this.Tabs.TabItems.Clear();
                foreach (var tab in this.App.Config.Tabs) {
                    this.AddTab(tab);
                }
            });
        }

        private void AddTab(Tab tab) {
            var grid = new Grid {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var messagePanel = new StackPanel {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4),
            };
            foreach (var msg in tab.Messages) {
                var block = new Controls.MessageTextBlock();
                block.Message = msg;
                block.ProcessMarkdown = tab.ProcessMarkdown;
                messagePanel.Children.Add(block);
            }
            var scrollViewer = new ScrollViewer {
                Content = messagePanel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var chatCard = new Border {
                Child = scrollViewer,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, 255, 255, 255)),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(210, 24, 26, 32)),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(chatCard, 0);
            grid.Children.Add(chatCard);

            tab.CollectionChanged += (s, e) => {
                this.App.Dispatch(() => {
                    if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
                        int index = e.NewStartingIndex;
                        foreach (var item in e.NewItems) {
                            if (item is ServerMessage msg) {
                                var block = new Controls.MessageTextBlock();
                                block.Message = msg;
                                block.ProcessMarkdown = tab.ProcessMarkdown;
                                if (index >= 0 && index <= messagePanel.Children.Count) {
                                    messagePanel.Children.Insert(index, block);
                                    index++;
                                } else {
                                    messagePanel.Children.Add(block);
                                }
                            }
                        }
                        messagePanel.DispatcherQueue?.TryEnqueue(() => {
                            _ = scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null);
                        });
                    } else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null) {
                        int index = e.OldStartingIndex;
                        for (int i = 0; i < e.OldItems.Count; i++) {
                            if (index >= 0 && index < messagePanel.Children.Count) {
                                messagePanel.Children.RemoveAt(index);
                            } else if (messagePanel.Children.Count > 0) {
                                messagePanel.Children.RemoveAt(0);
                            }
                        }
                    } else if (e.Action == NotifyCollectionChangedAction.Reset) {
                        messagePanel.Children.Clear();
                        foreach (var msg in tab.Messages) {
                            var block = new Controls.MessageTextBlock();
                            block.Message = msg;
                            block.ProcessMarkdown = tab.ProcessMarkdown;
                            messagePanel.Children.Add(block);
                        }
                    }
                });
            };

            var channelText = new TextBlock {
                Margin = new Thickness(8, 4, 0, 0),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK"),
            };
            channelText.Tapped += this.Channel_Tapped;
            channelText.SetBinding(
                TextBlock.TextProperty,
                new Binding {
                    Path = new PropertyPath("App.Connection.CurrentChannel"),
                    Source = this,
                    Mode = BindingMode.OneWay,
                }
            );
            channelText.ContextFlyout = this.CreateChannelFlyout();
            Grid.SetRow(channelText, 1);
            grid.Children.Add(channelText);

            var inputBox = new TextBox {
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK"),
            };
            inputBox.SetBinding(
                TextBox.PlaceholderTextProperty,
                new Binding {
                    Path = new PropertyPath("InputPlaceholder"),
                    Source = this,
                    Mode = BindingMode.OneWay,
                }
            );
            inputBox.SetBinding(
                TextBox.IsEnabledProperty,
                new Binding {
                    Path = new PropertyPath("App.Connection.Available"),
                    Source = this,
                    Mode = BindingMode.OneWay,
                }
            );
            inputBox.KeyDown += this.Input_Submit;
            Grid.SetRow(inputBox, 2);
            grid.Children.Add(inputBox);

            var editItem = new MenuFlyoutItem {
                Text = "编辑选项卡与过滤规则...",
                Icon = new FontIcon { Glyph = "\uE70F" }
            };
            editItem.Click += (s, e) => {
                var dialog = new ManageTab(tab);
                dialog.Activate();
            };

            var deleteItem = new MenuFlyoutItem {
                Text = "删除当前选项卡",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += (s, e) => {
                if (this.App.Config.Tabs.Count > 1) {
                    this.App.Config.Tabs.Remove(tab);
                    this.App.Config.Save();
                }
            };

            var flyout = new MenuFlyout();
            flyout.Items.Add(editItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(deleteItem);

            var tabViewItem = new TabViewItem {
                Header = new TextBlock {
                    Text = tab.Name,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK")
                },
                Content = grid,
                Tag = tab,
                ContextFlyout = flyout,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            tabViewItem.DoubleTapped += (s, e) => {
                var dialog = new ManageTab(tab);
                dialog.Activate();
            };

            this.Tabs.TabItems.Add(tabViewItem);
        }

        private MenuFlyout CreateChannelFlyout() {
            var flyout = new MenuFlyout();
            flyout.Items.Add(CreateMenuItem("悄悄话", this.Channel_Tell));
            flyout.Items.Add(CreateMenuItem("说话", this.Channel_Say));
            flyout.Items.Add(CreateMenuItem("小队", this.Channel_Party));
            flyout.Items.Add(CreateMenuItem("团队", this.Channel_Alliance));
            flyout.Items.Add(CreateMenuItem("呼喊", this.Channel_Yell));
            flyout.Items.Add(CreateMenuItem("喊话", this.Channel_Shout));
            flyout.Items.Add(CreateMenuItem("部队", this.Channel_FreeCompany));
            flyout.Items.Add(CreateMenuItem("战队", this.Channel_PvpTeam));
            flyout.Items.Add(CreateMenuItem("新人频道", this.Channel_NoviceNetwork));
            flyout.Items.Add(new MenuFlyoutSeparator());
            for (int i = 1; i <= 8; i++) {
                var idx = i;
                flyout.Items.Add(CreateMenuItem($"跨服贝频道 [{i}]", (s, e) => this.App.Connection?.ChangeChannel((InputChannel)((int)InputChannel.CrossLinkshell1 + idx - 1))));
            }
            flyout.Items.Add(new MenuFlyoutSeparator());
            for (int i = 1; i <= 8; i++) {
                var idx = i;
                flyout.Items.Add(CreateMenuItem($"通讯贝 [{i}]", (s, e) => this.App.Connection?.ChangeChannel((InputChannel)((int)InputChannel.Linkshell1 + idx - 1))));
            }
            return flyout;
        }

        private static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler handler) {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += handler;
            return item;
        }

        public void ClearAllMessages() {
            this.Messages.Clear();
            foreach (var tab in this.App.Config.Tabs) {
                tab.ClearMessages();
            }
        }

        public void AddSystemMessage(string content) {
            var message = new ServerMessage(
                DateTime.UtcNow,
                0,
                new byte[0],
                Encoding.UTF8.GetBytes(content),
                new List<Chunk> {
                    new TextChunk(content) {
                        Foreground = 0xb38cffff,
                    },
                }
            );
            this.AddMessage(message);
        }

        private int lastSequence = -1;
        private int insertAt;

        public void AddReversedChunk(ServerMessage[] messages, int sequence) {
            if (sequence != this.lastSequence) {
                this.lastSequence = sequence;
                this.insertAt = this.Messages.Count;
            }

            // add messages to main list
            this.Messages.InsertRange(this.insertAt, messages);
            // add message to each tab if the filter allows for it
            foreach (var tab in this.App.Config.Tabs) {
                tab.AddReversedChunk(messages, sequence, this.App.Config);
            }

            var diff = this.Messages.Count - this.App.Config.LocalBacklogMessages;
            if (diff > 0) {
                this.Messages.RemoveRange(0, (int)diff);
            }
        }

        public void AddMessage(ServerMessage message) {
            // add message to main list
            this.Messages.Add(message);
            // add message to each tab if the filter allows for it
            foreach (var tab in this.App.Config.Tabs) {
                tab.AddMessage(message, this.App.Config);
            }

            var diff = this.Messages.Count - this.App.Config.LocalBacklogMessages;
            if (diff > 0) {
                this.Messages.RemoveRange(0, (int)diff);
            }
        }

        public void InsertTellCommand(string name, string world, bool focus = true) {
            var input = this.GetCurrentInputBox();
            if (input == null) {
                return;
            }

            var tell = $"/tell {name}@{world} ";

            input.Text = input.Text.Insert(0, tell);
            input.SelectionStart = tell.Length;
            input.SelectionLength = input.Text.Length - tell.Length;

            if (focus) {
                input.Focus(FocusState.Programmatic);
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e) {
            var dialog = new ConnectDialog();
            dialog.Activate();
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e) {
            this.App.Disconnect();
        }

        private void Input_Submit(object sender, KeyRoutedEventArgs e) {
            if (!(sender is TextBox textBox)) {
                return;
            }

            switch (e.Key) {
                case Windows.System.VirtualKey.Enter:
                    this.Submit(textBox);
                    break;
                case Windows.System.VirtualKey.Up:
                    this.ArrowNavigate(textBox, true);
                    break;
                case Windows.System.VirtualKey.Down:
                    this.ArrowNavigate(textBox, false);
                    break;
            }
        }

        private void Submit(TextBox textBox) {
            var conn = this.App.Connection;
            if (conn == null) {
                return;
            }

            conn.SendMessage(textBox.Text);
            this.History.Add(textBox.Text);
            while (this.History.Count > 100) {
                this.History.RemoveAt(0);
            }

            textBox.Text = "";
        }

        private void ArrowNavigate(TextBox textBox, bool up) {
            if (this.History.Count == 0) {
                return;
            }

            if (this.HistoryIndex == -1) {
                this.HistoryBuffer = textBox.Text;
            }

            if (up) {
                // go up in history
                this.HistoryIndex += 1;
                textBox.Text = this.History[this.ReverseHistoryIndex];
            } else {
                // go down in history
                this.HistoryIndex -= 1;

                if (this.HistoryIndex == -1) {
                    textBox.Text = this.HistoryBuffer;
                    this.HistoryBuffer = null;
                } else {
                    textBox.Text = this.History[this.ReverseHistoryIndex];
                }
            }
        }

        private void Configuration_Click(object sender, RoutedEventArgs e) {
            var dialog = new ConfigWindow(this.App.Config);
            dialog.Activate();
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Handle tab selection change
        }

        private void Tabs_AddTabButtonClick(TabView sender, object args) {
            var dialog = new ManageTab(null);
            dialog.Activate();
        }

        private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) {
            if (this.App.Config.Tabs.Count <= 1) {
                return;
            }
            if (args.Tab.Tag is Tab tab) {
                this.App.Config.Tabs.Remove(tab);
                this.App.Config.Save();
            }
        }

        public TextBox? GetCurrentInputBox() {
            if (this.Tabs.SelectedItem is TabViewItem tabViewItem && tabViewItem.Content is Grid grid) {
                foreach (var child in grid.Children) {
                    if (child is TextBox textBox) {
                        return textBox;
                    }
                }
            }
            return null;
        }

        private void Map_Click(object sender, RoutedEventArgs e) {
            MapWindow.ShowMap(this.CurrentPlayerData?.mapId, this.CurrentPlayerData?.mapX, this.CurrentPlayerData?.mapY, this.LocationText?.Text, this.CurrentPlayerData?.mapFilenameId, this.CurrentPlayerData?.mapSizeFactor);
        }

        private void LocationBtn_Click(object sender, RoutedEventArgs e) {
            MapWindow.ShowMap(this.CurrentPlayerData?.mapId, this.CurrentPlayerData?.mapX, this.CurrentPlayerData?.mapY, this.LocationText?.Text, this.CurrentPlayerData?.mapFilenameId, this.CurrentPlayerData?.mapSizeFactor);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Export_Click(object sender, RoutedEventArgs e) {
            var dialog = new Export();
            dialog.Activate();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        private void Channel_Tapped(object sender, TappedRoutedEventArgs e) {
            e.Handled = true;
        }

        private void Channel_Tell(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Tell);
        }

        private void Channel_Say(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Say);
        }

        private void Channel_Party(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Party);
        }

        private void Channel_Alliance(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Alliance);
        }

        private void Channel_Yell(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Yell);
        }

        private void Channel_Shout(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Shout);
        }

        private void Channel_FreeCompany(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.FreeCompany);
        }

        private void Channel_PvpTeam(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.PvpTeam);
        }

        private void Channel_NoviceNetwork(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.NoviceNetwork);
        }

        private void Channel_CrossLinkshell1(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell1);
        }

        private void Channel_CrossLinkshell2(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell2);
        }

        private void Channel_CrossLinkshell3(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell3);
        }

        private void Channel_CrossLinkshell4(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell4);
        }

        private void Channel_CrossLinkshell5(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell5);
        }

        private void Channel_CrossLinkshell6(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell6);
        }

        private void Channel_CrossLinkshell7(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell7);
        }

        private void Channel_CrossLinkshell8(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.CrossLinkshell8);
        }

        private void Channel_Linkshell1(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell1);
        }

        private void Channel_Linkshell2(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell2);
        }

        private void Channel_Linkshell3(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell3);
        }

        private void Channel_Linkshell4(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell4);
        }

        private void Channel_Linkshell5(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell5);
        }

        private void Channel_Linkshell6(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell6);
        }

        private void Channel_Linkshell7(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell7);
        }

        private void Channel_Linkshell8(object sender, RoutedEventArgs e) {
            this.App.Connection?.ChangeChannel(InputChannel.Linkshell8);
        }

        private bool Not(bool value) => !value;
    }
}
