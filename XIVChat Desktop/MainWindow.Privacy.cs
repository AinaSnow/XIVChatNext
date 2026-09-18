using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using XIVChatCommon.Message;
using XIVChatCommon.Presentation;

namespace XIVChat_Desktop;

public partial class MainWindow {
    private void UpdatePrivacyDisplay() {
        if (!initialized) return;
        StreamerButton.IsChecked = App.Presentation.Enabled;
        StreamerButton.Content = L(App.Presentation.Enabled ? "Privacy.Active" : "Privacy.Title");
        ToolTipService.SetToolTip(StreamerButton, L("Privacy.Help"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StreamerButton, (string)StreamerButton.Content);
        ToolTipService.SetToolTip(NicknameButton, L("Contact.Nickname"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NicknameButton, L("Contact.Nickname"));
        RefreshFriendRows(); UpdateNavigation(); UpdateReady();
        if (selectedConversation is { } model) { ChatTitle.Text = model.Name; ChatSubtitle.Text = model.World; }
        else { ChatTitle.Text = App.Presentation.Text(ChatTitle.Text); ChatSubtitle.Text = App.Presentation.Text(ChatSubtitle.Text); }
        for (int i = 0; i < historyResults.Count; i++) historyResults[i] = historyResults[i] with { };
        for (int i = 0; i < eventRows.Count; i++) eventRows[i] = new EventListItem(eventRows[i].Row);
        RefreshOwnerLabels();
        FavoritesList.ItemsSource = null; FavoritesList.ItemsSource = cardFavorites;
        UpdatePlayerDisplay();
    }
    private void RefreshOwnerLabels() {
        var selected = (HistoryOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
        historyOwners = historyOwners.Select(o => o.Owner is { } owner ? new HistoryOwnerOption(
            App.Presentation.OwnerLabel(owner.Source, owner.OwnerKey, owner.Identity) + " · " + owner.Source[..Math.Min(8, owner.Source.Length)], owner) : o).ToList();
        historySyncing = true;
        HistoryOwnerPicker.ItemsSource = historyOwners;
        HistoryOwnerPicker.SelectedItem = historyOwners.FirstOrDefault(o => o.Owner?.Source == selected?.Source && o.Owner?.OwnerKey == selected?.OwnerKey);
        historySyncing = false;
        if (EventOwnerPicker.ItemsSource is System.Collections.Generic.IEnumerable<HistoryOwnerOption> options) {
            var current = (EventOwnerPicker.SelectedItem as HistoryOwnerOption)?.Owner;
            var labels = options.Select(o => o.Owner is { } owner ? new HistoryOwnerOption(
                App.Presentation.OwnerLabel(owner.Source, owner.OwnerKey, owner.Identity) + " · " + owner.Source[..Math.Min(8, owner.Source.Length)], owner) : o).ToList();
            eventSyncing = true; EventOwnerPicker.ItemsSource = labels;
            EventOwnerPicker.SelectedItem = labels.FirstOrDefault(o => o.Owner?.Source == current?.Source && o.Owner?.OwnerKey == current?.OwnerKey);
            eventSyncing = false;
        }
    }
    internal void UpdatePlayerDisplay() {
        var owner = App.Session.Player?.Identity ?? App.Workbench.Owner;
        LoggedInAs.Text = owner == null ? L("Status.Disconnected") : App.Presentation.Identity(owner).Name;
        var showWorld = owner != null && !App.Presentation.Hidden(owner);
        CurrentWorld.Text = showWorld ? App.Session.Player?.currentWorld ?? owner?.HomeWorld ?? "" : "";
        CurrentWorld.Visibility = CurrentWorldSeparator.Visibility = LoggedInAsSeparator.Visibility = V(showWorld);
    }
    private void Streamer_Click(object sender, RoutedEventArgs e) {
        var prior = App.Config.Privacy.Copy();
        try {
            App.Config.Privacy.Enabled = StreamerButton.IsChecked == true;
            if (App.Config.Privacy.Enabled && !App.Config.Privacy.HideSelf && !App.Config.Privacy.HideOthers)
                App.Config.Privacy.HideSelf = App.Config.Privacy.HideOthers = true;
            App.Config.Save();
        } catch (Exception) {
            App.Config.Privacy = prior; UpdatePrivacyDisplay(); FooterStatus.Text = L("Privacy.SaveFailed");
        }
    }
    private void Nickname_Click(object sender, RoutedEventArgs e) {
        if (selectedConversation is { } model) _ = EditNicknameAsync(model.Peer, App.Presentation.Context(model.State.Source, model.State.OwnerKey));
    }
    private void Contact_RightTapped(object sender, RightTappedRoutedEventArgs e) {
        if (sender is not FrameworkElement element) return;
        var peer = element.DataContext switch { FriendRow friend => friend.Peer, ConversationModel model => model.Peer, _ => null };
        if (peer == null) return;
        var context = element.DataContext is ConversationModel conversation ? App.Presentation.Context(conversation.State.Source, conversation.State.OwnerKey) : App.Presentation.Current;
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = L("Contact.Nickname") };
        edit.Click += async (_, _) => await EditNicknameAsync(peer, context); menu.Items.Add(edit);
        menu.ShowAt(element, e.GetPosition(element)); e.Handled = true;
    }
    private async Task EditNicknameAsync(CharacterIdentity peer, DisplayContext context) {
        if (dialogOpen) return;
        // An existing remark may contain a real-life name; don't reveal it in an editor on stream.
        if (App.Presentation.Enabled) { FooterStatus.Text = L("Privacy.EditAfterDisable"); return; }
        dialogOpen = true;
        try {
            var input = new TextBox { Text = App.Presentation.Engine.Nickname(context, peer), MaxLength = 64, AcceptsReturn = false, Header = L("Contact.NicknameHelp") };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(input); panel.Children.Add(error);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L("Contact.Nickname") + " · " + App.Presentation.Identity(peer, context).Name,
                Content = panel, PrimaryButtonText = L("Dialog.Save"), CloseButtonText = L("Dialog.Cancel") };
            dialog.PrimaryButtonClick += async (_, args) => {
                var deferral = args.GetDeferral();
                try { await App.Presentation.SetNicknameAsync(context, peer, input.Text); }
                catch (Exception) { args.Cancel = true; error.Text = L("Contact.SaveFailed"); }
                finally { deferral.Complete(); }
            };
            void CloseOnPrivacyChange() { if (App.Presentation.Enabled) dialog.Hide(); }
            App.Presentation.PolicyChanged += CloseOnPrivacyChange;
            try { await dialog.ShowAsync(); }
            finally { App.Presentation.PolicyChanged -= CloseOnPrivacyChange; }
        } finally { dialogOpen = false; }
    }
}
