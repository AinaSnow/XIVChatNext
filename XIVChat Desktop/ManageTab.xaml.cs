using System;
using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XIVChat_Desktop {
    public partial class ManageTab : Window {
        public App App => (App)Application.Current;

        public Tab Tab { get; }

        private readonly bool isNewTab;
        private readonly IImmutableSet<FilterType> oldFilters;

        public ManageTab(Tab? tab) {
            this.isNewTab = tab == null;
            this.Tab = tab ?? new Tab("") {
                Filter = Tab.GeneralFilter(),
            };
            this.oldFilters = this.Tab.Filter.Types.ToImmutableHashSet();

            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);

            if (this.isNewTab) {
                this.Title = "添加选项卡";
            }

            foreach (var category in (FilterCategory[])Enum.GetValues(typeof(FilterCategory))) {
                var panel = new StackPanel {
                    Margin = new Thickness(8),
                    Orientation = Orientation.Vertical,
                };

                var tabContent = new ScrollViewer {
                    Content = panel,
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

                void SetAllChecked(bool isChecked) {
                    foreach (var child in panel.Children) {
                        if (!(child is CheckBox)) {
                            continue;
                        }

                        var check = (CheckBox)child;
                        check.IsChecked = isChecked;
                    }
                }

                buttonsPanel.Children.Add(selectButton);
                buttonsPanel.Children.Add(deselectButton);

                panel.Children.Add(buttonsPanel);
                panel.Children.Add(new Border { Height = 1, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), Margin = new Thickness(0, 4, 0, 4) });

                foreach (var type in category.Types()) {
                    var check = new CheckBox {
                        Content = type.Name(),
                        IsChecked = this.Tab.Filter.Types.Contains(type),
                    };

                    check.Checked += (sender, e) => {
                        this.Tab.Filter.Types.Add(type);
                    };
                    check.Unchecked += (sender, e) => {
                        this.Tab.Filter.Types.Remove(type);
                    };

                    panel.Children.Add(check);
                }

                var tabItem = new TabViewItem {
                    Header = new TextBlock { Text = category.Name() },
                    Content = tabContent,
                };

                this.Tabs.TabItems.Add(tabItem);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            if (this.TabName.Text.Length == 0) {
                // TODO: Show error dialog
                return;
            }

            this.Tab.Name = this.TabName.Text;
            this.Tab.ProcessMarkdown = this.MarkdownToggle.IsChecked ?? false;

            if (this.isNewTab) {
                this.App.Config.Tabs.Add(this.Tab);
            }

            if (this.isNewTab || !this.oldFilters.SetEquals(this.Tab.Filter.Types)) {
                this.Tab.RepopulateMessages(this.App.Window.Messages);
            }

            this.App.Config.Save();
            this.Close();
        }
    }
}
