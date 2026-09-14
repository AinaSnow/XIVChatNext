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
            Localize.BindWindow(this, () => this.Title = LocalizationHelper.GetString(this.isNewTab ? "ManageTab.New" : "ManageTab.Title"));

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
                };
                Localize.SetContent(selectButton, "Export.SelectAll");
                selectButton.Click += (sender, e) => SetAllChecked(true);

                var deselectButton = new Button {
                    Margin = new Thickness(4, 0, 0, 0),
                };
                Localize.SetContent(deselectButton, "Export.DeselectAll");
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
                        IsChecked = this.Tab.Filter.Types.Contains(type),
                    };

                    Localize.SetContent(check, "Filter." + type);
                    check.Checked += (sender, e) => {
                        this.Tab.Filter.Types.Add(type);
                    };
                    check.Unchecked += (sender, e) => {
                        this.Tab.Filter.Types.Remove(type);
                    };

                    panel.Children.Add(check);
                }

                var tabItem = new TabViewItem {
                    Content = tabContent,
                    IsClosable = false,
                };

                Localize.SetHeader(tabItem, "FilterCategory." + category);
                this.Tabs.TabItems.Add(tabItem);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrWhiteSpace(this.TabName.Text)) {
                ValidationError.Text = SetupText.T("请输入频道视图名称。", "Enter a channel view name.");
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
