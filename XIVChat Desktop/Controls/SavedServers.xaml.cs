using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace XIVChat_Desktop.Controls {
    public partial class SavedServers : UserControl {
        public App App => (App)Application.Current;
        private Configuration Config => this.App.Config;

        public IEnumerable<SavedServer> ItemsSource {
            get { return (IEnumerable<SavedServer>)this.GetValue(ItemsSourceProperty); }
            set { this.SetValue(ItemsSourceProperty, value); }
        }

        public Visibility ControlsVisibility {
            get { return (Visibility)this.GetValue(ControlsVisibilityProperty); }
            set { this.SetValue(ControlsVisibilityProperty, value); }
        }

        public SavedServer? SelectedServer {
            get {
                var item = this.Servers.SelectedItem;
                return item as SavedServer;
            }
        }

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            "ItemsSource",
            typeof(IEnumerable<SavedServer>),
            typeof(SavedServers),
            new PropertyMetadata(null)
        );

        public static readonly DependencyProperty ControlsVisibilityProperty = DependencyProperty.Register(
            "ControlsVisibility",
            typeof(Visibility),
            typeof(SavedServers),
            new PropertyMetadata(Visibility.Visible)
        );

        public SavedServers() {
            this.InitializeComponent();
        }

        private void AddServer_Click(object sender, RoutedEventArgs e) {
            var window = new ManageServer(null);
            window.Activate();
        }

        private void DeleteServer_Click(object sender, RoutedEventArgs e) {
            var server = this.SelectedServer;
            if (server == null) {
                return;
            }

            this.Config.Servers.Remove(server);
            this.Config.Save();
        }

        private void EditServer_Click(object sender, RoutedEventArgs e) {
            var server = this.SelectedServer;
            if (server == null) {
                return;
            }

            var window = new ManageServer(server);
            window.Activate();
        }

        private void Item_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) {
            var server = ((FrameworkElement)e.OriginalSource).DataContext;
            if (!(server is SavedServer)) {
                return;
            }

            this.ItemDoubleClick?.Invoke((SavedServer)server);
        }

        public delegate void DoubleTapHandler(SavedServer server);

        public event DoubleTapHandler? ItemDoubleClick;
    }
}
