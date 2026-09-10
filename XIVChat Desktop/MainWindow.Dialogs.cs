using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    public partial class MainWindow {
        private bool dialogOpen;
        private async Task<string?> EditTextAsync(string title, string value) {
            if (dialogOpen) return null;
            dialogOpen = true;
            try {
                var input = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 360, MaxLength = 8192 };
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = title, Content = input,
                    PrimaryButtonText = L("Dialog.Save"), CloseButtonText = L("Dialog.Cancel") };
                return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
            } finally { dialogOpen = false; }
        }
        private async void ConversationNote_Click(object sender, RoutedEventArgs e) {
            if (selectedConversation is not { } model) return;
            var text = await EditTextAsync(L("Conversation.Note") + " · " + model.Name, model.Note);
            if (text != null) { model.Note = text; App.Workbench.Save(model); FilterConversations(); }
        }
        private async void AddView_Click(object sender, RoutedEventArgs e) {
            if (section == "channels") { new ManageTab(null).Activate(); return; }
            if (App.Workbench.OwnerKey == null) { FooterStatus.Text = L("Workbench.ConnectFirst"); return; }
            if (dialogOpen) return;
            dialogOpen = true;
            try {
                var name = new TextBox { Header = L("Conversation.Name"), MaxLength = 64 };
                var worlds = Enumerable.Range(1, ushort.MaxValue).Select(i => (Id: (ushort)i, Name: Util.WorldName((ushort)i)))
                    .Where(w => !string.IsNullOrEmpty(w.Name) && w.Name.All(char.IsLetter) && !char.IsLower(w.Name[0])).OrderBy(w => w.Name).ToArray();
                var world = new ComboBox { Header = L("Conversation.World"), ItemsSource = worlds.Select(w => w.Name).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(name); panel.Children.Add(world); panel.Children.Add(error);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L("Conversation.New"), Content = panel,
                    PrimaryButtonText = L("Conversation.Open"), CloseButtonText = L("Dialog.Cancel") };
                CharacterIdentity? peer = null;
                dialog.PrimaryButtonClick += (_, args) => {
                    peer = world.SelectedIndex >= 0 ? new CharacterIdentity { Name = name.Text.Trim(), HomeWorldId = worlds[world.SelectedIndex].Id, HomeWorld = worlds[world.SelectedIndex].Name! } : null;
                    if (peer == null || !TellTarget.From(peer).IsValid) { args.Cancel = true; error.Text = L("Conversation.InvalidIdentity"); }
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && peer != null && App.Workbench.Open(peer) is { } model) ShowConversation(model);
            } finally { dialogOpen = false; }
        }
        private void Avatar_Click(object sender, RoutedEventArgs e) { if (selectedConversation is { } model) _ = EditAvatarAsync(model.Peer); }
        private void Account_Click(object sender, RoutedEventArgs e) { if ((App.Session.Player?.Identity ?? App.Workbench.Owner) is { } identity) _ = EditAvatarAsync(identity); }
        private async Task EditAvatarAsync(CharacterIdentity identity) {
            if (dialogOpen) return;
            dialogOpen = true;
            try {
                var mapping = App.Session.Store is { } store ? await store.GetAvatarAsync(ConversationIdentity.PeerKey(identity)!) : null;
                var input = new TextBox { Header = L("Avatar.Id"), Text = mapping?.LodestoneId ?? "", MaxLength = 256 };
                var help = new TextBlock { Text = L("Avatar.Help"), TextWrapping = TextWrapping.Wrap };
                var state = new TextBlock { Text = L(mapping?.Disabled == true ? "Avatar.Disabled" : mapping?.ImagePath != null ? "Avatar.Cached" : "Avatar.Placeholder"), TextWrapping = TextWrapping.Wrap };
                var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(help); panel.Children.Add(input); panel.Children.Add(state);
                if (mapping?.LodestoneId is { } id) panel.Children.Add(new HyperlinkButton { Content = L("Avatar.Profile"), NavigateUri = LodestoneParser.ProfileUrl(id), Padding = new Thickness(0) });
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L("Avatar.Title") + " · " + identity.Name,
                    Content = panel, PrimaryButtonText = L("Dialog.Save"), SecondaryButtonText = L("Avatar.Auto"), CloseButtonText = L("Dialog.Cancel") };
                dialog.PrimaryButtonClick += (_, args) => {
                    if (!string.IsNullOrWhiteSpace(input.Text) && LodestoneParser.ExtractId(input.Text) == null) { args.Cancel = true; state.Text = L("Avatar.Invalid"); }
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary) await App.Avatars.BindAsync(identity, string.IsNullOrWhiteSpace(input.Text) ? null : input.Text);
                else if (result == ContentDialogResult.Secondary) await App.Avatars.BindAsync(identity, null, true);
            } catch (Exception ex) { FooterStatus.Text = L("Avatar.Unavailable") + " " + ex.Message; }
            finally { dialogOpen = false; }
        }
    }
}
