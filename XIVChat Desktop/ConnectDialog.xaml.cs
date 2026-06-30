using Microsoft.UI.Xaml;
using XIVChat_Desktop.Controls;

namespace XIVChat_Desktop {
    public partial class ConnectDialog : Window {
        public App App => (App)Application.Current;

        public ConnectDialog() {
            this.InitializeComponent();
            ThemeHelper.InitializeWindow(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 400));
        }

        private void Connect_Clicked(object sender, RoutedEventArgs e) {
            this.ConnectTo(this.Servers.SelectedServer);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        private void Servers_ItemDoubleClick(SavedServer? server) {
            this.ConnectTo(server);
        }

        private void ConnectTo(SavedServer? server) {
            if (server == null) {
                return;
            }

            this.App.Connect(server.Host, server.Port);
            this.Close();
        }
    }
}
