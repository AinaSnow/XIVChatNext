using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using XIVChatCommon.Message;
using XIVChatStorage;

namespace XIVChat_Desktop;

public partial class MainWindow {
    private void Popout_Click(object sender, RoutedEventArgs e) => PopoutCurrent();
    private void Layouts_Click(object sender, RoutedEventArgs e) => new WorkspaceLayoutWindow(App).Activate();
    internal ChatPopoutWindow? PopoutCurrent() {
        if (ChatPanel.Visibility != Visibility.Visible || ComposerPanel.Visibility != Visibility.Visible) return null;
        var source = selectedConversation?.State.Source ?? (App.Workbench.Source.Length > 0 ? App.Workbench.Source : App.Session.Source);
        var owner = selectedConversation?.State.OwnerKey ?? App.Workbench.OwnerKey ?? "unassigned:" + source;
        if (selectedConversation == null && selectedChannel == null) return null;
        return App.Workspace.Open(new ChatWindowState {
            Source = source, OwnerKey = owner, Owner = App.Workbench.Owner == null ? null : ConversationIdentity.Copy(App.Workbench.Owner), Peer = selectedConversation == null ? null : ConversationIdentity.Copy(selectedConversation.Peer),
            ChannelId = selectedConversation == null ? selectedChannel?.Id : null,
            Name = selectedConversation?.Name ?? selectedChannel!.Name,
            FontSize = Math.Clamp(App.Config.FontSize, 8, 48),
            Markdown = selectedConversation == null ? selectedChannel!.ProcessMarkdown : true,
            Bounds = new(AppWindow.Position.X + 100, AppWindow.Position.Y + 80, 640, 640), Open = true,
        });
    }
    internal void ReturnPopout(ChatWindowState state) {
        if (state.Peer != null) {
            if (App.Workbench.Source == state.Source && App.Workbench.OwnerKey == state.OwnerKey && App.Workbench.Open(state.Peer) is { } model) {
                Navigate("conversations"); ShowConversation(model);
            } else FooterStatus.Text = L("Conversation.ReadOnly");
        } else if (App.Config.Tabs.FirstOrDefault(t => t.Id == state.ChannelId) is { } tab) {
            Navigate("channels"); ChannelList.SelectedItem = tab;
        }
    }
    internal void CaptureWorkspaceState(WorkspaceLayout layout) {
        SaveComposer();
        layout.MainDrafts = new(channelDrafts);
        layout.MainSection = section;
        var source = selectedConversation?.State.Source ?? (App.Workbench.Source.Length > 0 ? App.Workbench.Source : App.Session.Source);
        var owner = selectedConversation?.State.OwnerKey ?? App.Workbench.OwnerKey ?? "unassigned:" + source;
        layout.MainView = section is "channels" or "conversations" or "friends" && (selectedConversation != null || selectedChannel != null)
            ? new ChatWindowState { Source = source, OwnerKey = owner, Owner = App.Workbench.Owner, Peer = selectedConversation?.Peer,
                ChannelId = selectedConversation == null ? selectedChannel?.Id : null, Name = selectedConversation?.Name ?? selectedChannel?.Name ?? "" } : null;
    }
    internal void RestoreWorkspaceState(WorkspaceLayout layout) {
        channelDrafts.Clear(); foreach (var draft in layout.MainDrafts) channelDrafts[draft.Key] = draft.Value;
        if (layout.MainView is { Owner.IsComplete: true } view && App.Session.Player == null) App.Workbench.SetContext(view.Source, view.Owner);
        Navigate(layout.MainSection);
        if (layout.MainView is { } target && layout.MainSection is "channels" or "conversations" or "friends") ReturnPopout(target);
    }
    internal void CaptureWorkspaceComposer() => SaveComposer();
    internal void SetWorkspaceVisible(bool visible) {
        if (visible) { AppWindow.Show(); presenceTimer.Start(); }
        else { active = false; presenceTimer.Stop(); AppWindow.Hide(); }
    }
    public new void Close() => _ = App.Workspace.CloseMainAsync();
    internal void CloseForWorkspace() {
        stopping = true; allowClose = true; historyCancellation?.Cancel(); conversationLoad?.Cancel(); base.Close();
    }
    private void OpenExport() {
        if (section == "history" && cardSourceOrigin is { } origin) {
            new Export(new HistoryQuery(Source: origin.Source, OwnerKey: origin.OwnerKey, FavoriteId: origin.FavoriteId,
                RecordId: origin.FavoriteId != null ? null : origin.Message is { } message ? message.LocalStorageId ?? HistoryStore.StorageId(origin.Source, message) : ""), L("Card.MessageSources")).Activate(); return;
        }
        if (section == "history") {
            new Export(activeHistoryQuery ?? new HistoryQuery(), L("Workbench.History")).Activate(); return;
        }
        if (selectedConversation is { } conversation) {
            new Export(new HistoryQuery(Source: conversation.State.Source, OwnerKey: conversation.State.OwnerKey, PeerKey: conversation.Key), conversation.Name).Activate(); return;
        }
        var source = App.Workbench.Source.Length > 0 ? App.Workbench.Source : App.Session.Source;
        var owner = App.Workbench.OwnerKey ?? "unassigned:" + source;
        new Export(new HistoryQuery(Source: source, OwnerKey: owner), selectedChannel?.Name ?? L("Workbench.History"),
            selectedChannel == null ? null : new Filter { Types = selectedChannel.Filter.Types.ToHashSet() }).Activate();
    }
    private void InitializePopoutDrag() {
        bool dragging = false; Windows.Foundation.Point origin = default;
        PopoutDragHandle.PointerPressed += (_, e) => {
            var point = e.GetCurrentPoint(Root);
            if (!point.Properties.IsLeftButtonPressed) return;
            dragging = PopoutDragHandle.CapturePointer(e.Pointer); origin = point.Position; e.Handled = true;
        };
        PopoutDragHandle.PointerCanceled += (_, _) => { dragging = false; PopoutDragHandle.ReleasePointerCaptures(); };
        PopoutDragHandle.PointerCaptureLost += (_, _) => dragging = false;
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) => {
            if (e.Key == Windows.System.VirtualKey.Escape && dragging) { dragging = false; PopoutDragHandle.ReleasePointerCaptures(); e.Handled = true; }
        }), true);
        PopoutDragHandle.PointerReleased += (_, e) => {
            if (!dragging) return;
            var point = e.GetCurrentPoint(Root).Position; dragging = false; PopoutDragHandle.ReleasePointerCaptures();
            if (Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) < 20 ||
                point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight) return;
            if (PopoutCurrent() is not { } window) return;
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            WorkspaceWindows.ApplyBounds(window, new(AppWindow.Position.X + (int)(point.X * scale), AppWindow.Position.Y + (int)(point.Y * scale), 640, 640));
            e.Handled = true;
        };
    }
}
