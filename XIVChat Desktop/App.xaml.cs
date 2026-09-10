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
        private ChatSession? session;
        public ChatSession Session => this.session ??= new ChatSession(() => this.Config);
        private WorkbenchSession? workbench;
        public WorkbenchSession Workbench => this.workbench ??= new WorkbenchSession(this);
        private LodestoneAvatars? avatars;
        public LodestoneAvatars Avatars {
            get {
                if (this.avatars == null) {
                    this.avatars = new LodestoneAvatars(() => this.Session.Store,
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChatDesktop", "Avatars"), this.Config.OnlineAvatars);
                    this.Config.Saved += () => this.avatars.SetEnabled(this.Config.OnlineAvatars);
                }
                return this.avatars;
            }
        }
        private DispatcherQueue? dispatcher;
        private Task? connectionTask;

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

        private static Microsoft.Web.WebView2.Core.CoreWebView2Environment? _sharedWebViewEnv;

        public static async Task EnsureWebView2Async(Microsoft.UI.Xaml.Controls.WebView2 webView) {
            if (webView.CoreWebView2 != null) return;
            try {
                if (_sharedWebViewEnv == null) {
                    string udf = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChatDesktop", "WebView2UserData");
                    System.IO.Directory.CreateDirectory(udf);
                    _sharedWebViewEnv = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, udf, null);
                }
                await webView.EnsureCoreWebView2Async(_sharedWebViewEnv);
            } catch (Exception ex) {
                try {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "webview_init_error.log"), $"{DateTime.Now}: {ex}\n");
                } catch { }
                await webView.EnsureCoreWebView2Async();
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void InitializeRuntime() {
            try {
                string baseDir = AppContext.BaseDirectory;
                Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", baseDir);
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!path.Contains(baseDir, StringComparison.OrdinalIgnoreCase)) {
                    Environment.SetEnvironmentVariable("PATH", $"{baseDir};{path}");
                }
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
        private bool configRecoveredFromBackup;

        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            base.OnLaunched(args);
            this.dispatcher = DispatcherQueue.GetForCurrentThread();

            try {
                this.Config = Configuration.Load(out this.configRecoveredFromBackup) ?? new Configuration();
            } catch (Exception ex) {
                this.configLoadException = ex;
                this.Config = new Configuration();
            }

            LocalizationHelper.Initialize(this.Config.Language);

            try {
                // A failed load must never overwrite the original during startup.
                if (this.configLoadException == null && !this.configRecoveredFromBackup) this.Config.Save();
            } catch {
                // Ignore save error on launch
            }

            try {
                var historyPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVChatDesktop", "history.sqlite3");
                this.Session.Store = await XIVChatStorage.HistoryStore.OpenAsync(historyPath);
                await this.Session.Store.PruneAsync(this.Config.HistoryRetentionDays, DateTime.UtcNow);
            } catch (Exception ex) {
                this.Session.ReportStorageError(ex);
            }
            this.InitialiseWindow();
            if (this.Session.StorageError != null) this.Window.AddSystemMessage(LocalizationHelper.GetString("History.Unavailable") + " " + this.Session.StorageError.Message);
            this.Session.PersistenceFailed += ex => this.Dispatch(() => this.Window?.AddSystemMessage(LocalizationHelper.GetString("History.Unavailable") + " " + ex.Message));
        }

        public async void InitialiseWindow() {
            try {
                var wnd = new MainWindow();
                this.Window = wnd;
                ApplyTheme(this.Config.Theme);
                ApplyAlwaysOnTop(this.Config.AlwaysOnTop);
                wnd.Activate();

                if (this.configLoadException != null || this.configRecoveredFromBackup) {
                    var dialog = new ContentDialog {
                        Title = LocalizationHelper.GetString("ConfigRecovery.Title"),
                        Content = LocalizationHelper.GetString(this.configRecoveredFromBackup ? "ConfigRecovery.BackupLoaded" : "ConfigRecovery.DefaultsLoaded")
                            + "\n\n" + Configuration.ConfigFilePath,
                        PrimaryButtonText = this.configRecoveredFromBackup ? LocalizationHelper.GetString("ConfigRecovery.Restore") : "",
                        CloseButtonText = LocalizationHelper.GetString("Dialog.Close"),
                        XamlRoot = wnd.Content.XamlRoot
                    };
                    try {
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary) {
                            try {
                                this.Config.Save();
                            } catch (Exception ex) {
                                var errorDialog = new ContentDialog {
                                    Title = LocalizationHelper.GetString("ConfigRecovery.SaveFailed"),
                                    Content = ex.Message,
                                    CloseButtonText = LocalizationHelper.GetString("Dialog.Close"),
                                    XamlRoot = wnd.Content.XamlRoot
                                };
                                await errorDialog.ShowAsync();
                            }
                        }
                    } catch { }
                    this.configLoadException = null;
                    this.configRecoveredFromBackup = false;
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
            var queue = this.dispatcher ?? this.Window?.DispatcherQueue;
            if (queue != null) {
                queue.TryEnqueue(() => action());
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
            this.connectionTask = Task.Run(this.Connection.Connect);
        }

        public async Task StopSessionAsync() {
            this.Disconnect();
            if (this.connectionTask != null) await this.connectionTask;
            if (this.workbench != null) await this.workbench.FlushAsync();
            this.avatars?.Dispose();
            if (this.Session.Store != null) {
                try { await this.Session.Store.DisposeAsync(); }
                finally { this.Session.Store = null; }
            }
        }

        public async Task RestorePlayerHistoryAsync(PlayerData player) {
            var store = this.Session.Store;
            var source = this.Session.Source;
            var owner = player.Identity?.Key;
            if (store == null || owner == null || this.Session.StorageError != null) return;
            try {
                var recent = await store.SearchAsync(new XIVChatStorage.HistoryQuery(OwnerKey: owner, Source: source,
                    Limit: (int)Math.Min(500, this.Config.LocalBacklogMessages)));
                this.Dispatch(() => {
                    if (this.Session.Player?.Identity?.Key == owner && this.Session.Source == source)
                        this.Session.AddCursorPage(recent.Reverse().Select(row => row.Message).ToArray());
                });
            } catch (Exception ex) { this.Session.ReportStorageError(ex); }
        }

        public void Disconnect() {
            if (!this.Connected) {
                return;
            }

            var oldConn = this.Connection;
            this.Connection = null;
            oldConn?.Disconnect();
            this.Session.SetPlayer(null);
            this.Session.Friends.SetContext(this.Session.Source, null, null, false);

            this.Dispatch(() => {
                if (this.Window != null) {
                    this.Window.LoggedInAsText.Text = "未登录";
                    this.Window.LoggedInAsSeparatorText.Visibility = Visibility.Collapsed;
                    this.Window.CurrentWorldText.Visibility = Visibility.Collapsed;
                    this.Window.CurrentWorldSeparatorText.Visibility = Visibility.Collapsed;
                    this.Window.LocationButton.Visibility = Visibility.Collapsed;
                    this.Window.CurrentPlayerData = null;
                    this.Window.AddSystemMessage("已断开连接");
                    this.Window.OnPropertyChanged(nameof(MainWindow.InputPlaceholder));
                }
            });
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
