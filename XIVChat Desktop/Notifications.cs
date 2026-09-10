using System;
using System.IO;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace XIVChat_Desktop {
    internal sealed class WindowsNotificationSink : INotificationSink {
        private readonly Action<NotificationTarget> activate;
        public WindowsNotificationSink(Action<NotificationTarget> activate) {
            this.activate = activate;
            AppNotificationManager.Default.NotificationInvoked += Invoked;
            try {
                AppNotificationManager.Default.Register("XIVChat Next", new Uri(Path.Combine(AppContext.BaseDirectory, "Resources", "logo-c1.ico")));
                // Registration must precede activation inspection, including launches from a closed app.
                var args = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (args.Kind == ExtendedActivationKind.AppNotification && args.Data is AppNotificationActivatedEventArgs notification) Handle(notification);
            } catch { AppNotificationManager.Default.NotificationInvoked -= Invoked; throw; }
        }
        private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Handle(args);
        private void Handle(AppNotificationActivatedEventArgs args) {
            if (args.Arguments.TryGetValue("target", out var value) && NotificationTarget.Parse(value) is { } target) activate(target);
        }
        public bool Show(NotificationDelivery delivery) {
            var candidate = delivery.Candidate;
            var builder = new AppNotificationBuilder().AddArgument("target", candidate.Target.ToArgument())
                .SetAppLogoOverride(new Uri(Path.Combine(AppContext.BaseDirectory, "Resources", "logo-c1.png")))
                .AddText(candidate.Title).AddText(delivery.Count > 1
                    ? string.Format(LocalizationHelper.GetString("Notify.Batch"), delivery.Count, candidate.Text) : candidate.Text);
            if (!delivery.Sound) builder.MuteAudio();
            var notification = builder.BuildNotification();
            notification.Tag = delivery.Tag; notification.Group = "XIVChatNext";
            notification.Expiration = new DateTimeOffset(candidate.ExpiresAt ?? DateTime.UtcNow.AddHours(2));
            AppNotificationManager.Default.Show(notification);
            return notification.Id != 0;
        }
        public void Dispose() {
            AppNotificationManager.Default.NotificationInvoked -= Invoked;
            AppNotificationManager.Default.Unregister();
        }
    }
}
