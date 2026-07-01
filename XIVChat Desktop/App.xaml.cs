using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public partial class App : INotifyPropertyChanged {
        public MainWindow Window { get; private set; } = null!;
        public Configuration Config { get; private set; } = null!;

        public string? LastHost { get; set; }

        private Connection? connection;

        public Connection? Connection {
            get => this.connection;
            set {
                this.connection = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Connection)));
                this.ConnectionStatusChanged();
            }
        }

        public bool Connected => this.Connection != null;

        public event PropertyChangedEventHandler? PropertyChanged;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void InitializeRuntime() {
            try {
                Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            } catch { }
        }

        public App() {
            this.UnhandledException += (s, e) => {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "app_unhandled_crash.log"), e.Exception?.ToString() + "\nMessage: " + e.Message); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "appdomain_crash.log"), e.ExceptionObject?.ToString()); } catch { }
            };
            try {
                this.InitializeComponent();
            } catch (Exception ex) {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "app_init_crash.log"), ex.ToString()); } catch { }
                throw;
            }
        }

        private Exception? configLoadException;

        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            base.OnLaunched(args);

            try {
                this.Config = Configuration.Load() ?? new Configuration();
            } catch (Exception ex) {
                this.configLoadException = ex;
                this.Config = new Configuration();
            }

            LocalizationHelper.Initialize(this.Config.Language);

            try {
                this.Config.Save();
            } catch {
                // Ignore save error on launch
            }

            this.InitialiseWindow();
        }

        public async void InitialiseWindow() {
            try {
                var wnd = new MainWindow();
                this.Window = wnd;
                ApplyTheme(this.Config.Theme);
                ApplyAlwaysOnTop(this.Config.AlwaysOnTop);
                wnd.Activate();

                if (this.configLoadException != null) {
                    var dialog = new ContentDialog {
                        Title = "Error loading config",
                        Content = $"Could not load the configuration file: {this.configLoadException.Message}. A new default configuration has been created.",
                        CloseButtonText = "OK",
                        XamlRoot = wnd.Content.XamlRoot
                    };
                    try {
                        await dialog.ShowAsync();
                    } catch { }
                    this.configLoadException = null;
                }
            } catch (Exception ex) {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "initwindow_crash.log"), ex.ToString()); } catch { }
                throw;
            }
        }

        public static void ApplyTheme(Theme theme) {
            ThemeHelper.ApplyTheme(theme);
        }

        public static void ApplyAlwaysOnTop(bool onTop) {
            if (((App)Application.Current).Window?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) {
                presenter.IsAlwaysOnTop = onTop;
            }
        }

        public void Dispatch(Action action) {
            if (this.Window?.DispatcherQueue != null) {
                this.Window.DispatcherQueue.TryEnqueue(() => action());
            } else {
                action();
            }
        }

        private void ConnectionStatusChanged() {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Connected)));
        }

        public void Connect(string host, ushort port) {
            if (this.Connected) {
                return;
            }

            this.Connection = new Connection(this, host, port);
            this.Connection.ReceiveMessage += this.OnReceiveMessage;
            Task.Run(this.Connection.Connect);
        }

        public void Disconnect() {
            if (!this.Connected) {
                return;
            }

            this.Connection?.Disconnect();
            this.Connection = null;
        }

        private void OnReceiveMessage(ServerMessage message) {
            if (!this.Config.Notifications.Any(notif => notif.Matches(message))) {
                return;
            }

            var sender = message.GetSenderPlayer();

            string title;
            if (sender != null) {
                var name = sender.Name;

                if (sender.Server != 0) {
                    name += $" ({Util.WorldName(sender.Server)})";
                }

                title = name;
            } else {
                title = "Notification";
            }

            var text = message.ContentText;
            var attribution = message.Channel.Name();

            Win10Notify(title, text, attribution);
        }

        private static void Win10Notify(string title, string text, string? attribution) {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(text);

            if (attribution != null) {
                builder.AddText(attribution);
            }

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
    }
}
