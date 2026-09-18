using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using XIVChatCommon.Message;

namespace XIVChat_Desktop.Controls {
    public sealed class CharacterAvatar : UserControl {
        private readonly PersonPicture picture = new() { Width = 36, Height = 36 };
        private int version;
        private LodestoneAvatars? service;
        public static readonly DependencyProperty IdentityProperty = DependencyProperty.Register(nameof(Identity), typeof(CharacterIdentity), typeof(CharacterAvatar), new PropertyMetadata(null, Changed));
        public CharacterIdentity? Identity { get => (CharacterIdentity?)GetValue(IdentityProperty); set => SetValue(IdentityProperty, value); }
        public CharacterAvatar() {
            Content = picture;
            Loaded += (_, _) => { service = ((App)Application.Current).Avatars; service.Changed += AvatarChanged; ((App)Application.Current).Presentation.Changed += Refresh; Refresh(); };
            Unloaded += (_, _) => { version++; if (service != null) service.Changed -= AvatarChanged; service = null; ((App)Application.Current).Presentation.Changed -= Refresh; };
        }
        private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((CharacterAvatar)sender).Refresh();
        private void AvatarChanged(string key) {
            DispatcherQueue.TryEnqueue(() => { if (key == ConversationIdentity.PeerKey(Identity)) Refresh(); });
        }
        private async void Refresh() {
            var current = ++version;
            var presentation = ((App)Application.Current).Presentation;
            var display = presentation.Identity(Identity);
            picture.DisplayName = display.Name; picture.Initials = display.Hidden ? "?" : ""; picture.ProfilePicture = null;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picture, display.Name);
            if (display.Hidden) return;
            if (Identity == null || !IsLoaded || service == null) return;
            var active = service;
            var identity = ConversationIdentity.Copy(Identity);
            try {
                var mapping = await active.GetAsync(identity);
                if (current == version && IsLoaded && !presentation.Hidden(Identity) && active.ValidLocalPath(mapping?.ImagePath))
                    picture.ProfilePicture = new BitmapImage(new Uri(mapping!.ImagePath!));
            } catch { /* Keep the name placeholder when a cached file or profile cannot be read. */ }
        }
    }
}
