using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace XIVChat_Desktop {
    public partial class ManageTabs : Window {
        public App App => (App)Application.Current;

        private Tab? SelectedTab {
            get {
                var item = this.Tabs.SelectedItem;
                return item as Tab;
            }
        }

        public ManageTabs() {
            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
        }

        private void AddTab_Click(object sender, RoutedEventArgs e) {
            var dialog = new ManageTab(null);
            dialog.Activate();
        }

        private void EditTab_Click(object sender, RoutedEventArgs e) {
            var tab = this.SelectedTab;
            if (tab == null) {
                return;
            }
            var dialog = new ManageTab(tab);
            dialog.Activate();
        }

        private void DeleteTab_Click(object sender, RoutedEventArgs e) {
            var tab = this.SelectedTab;
            if (tab == null) {
                return;
            }

            this.App.Config.Tabs.Remove(tab);
            this.App.Config.Save();
        }

        private void Tab_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) {
            var item = ((FrameworkElement)e.OriginalSource).DataContext;
            if (!(item is Tab tab)) {
                return;
            }

            var dialog = new ManageTab(tab);
            dialog.Activate();
        }
    }
}
