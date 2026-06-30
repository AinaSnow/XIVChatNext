using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace XIVChat_Desktop {
    public static class Notifications {
        public static void Initialise() {
            // WinUI 3 notifications are initialized through AppNotificationManager
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }

        private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) {
            // TODO: Handle notification activation
        }

        public static void ShowNotification(string title, string text, string? attribution = null) {
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
