using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop.Controls {
    public sealed partial class ChatMessageList : UserControl, IDisposable {
        private readonly Tab tab;
        private readonly ObservableCollection<ChatMessageRow> rows = new();
        private ScrollViewer? scrollViewer;
        private bool followLatest = true;
        private bool scrollPending;
        private int scrollGeneration;
        private ChatMessageRow? pendingAnchor;
        private bool updating;
        private bool disposed;
        private int unreadCount;
        public bool FollowingLatest => this.followLatest;
        internal bool ScrollInProgress => scrollPending || pendingAnchor != null;
        public void ScrollToMessage(ServerMessage message) {
            var row = rows.FirstOrDefault(r => ReferenceEquals(r.Message, message) || message.MessageId != null && r.Message.MessageId == message.MessageId);
            if (row == null) return;
            followLatest = false;
            pendingAnchor = row;
            UpdateLocalizations();
            var generation = ++scrollGeneration;
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => {
                if (disposed || generation != scrollGeneration) return;
                MessageList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
                MessageList.UpdateLayout();
                pendingAnchor = null;
            })) pendingAnchor = null;
        }
        public string? CaptureScrollId() {
            if (followLatest || rows.Count == 0) return null;
            if (pendingAnchor != null) return pendingAnchor.Message.LocalStorageId;
            var index = (MessageList.ItemsPanelRoot as ItemsStackPanel)?.FirstVisibleIndex ?? 0;
            return rows[Math.Clamp(index, 0, rows.Count - 1)].Message.LocalStorageId;
        }
        public bool RestoreScroll(string? id, bool following) {
            followLatest = following;
            if (following) { scrollGeneration++; pendingAnchor = null; unreadCount = 0; UpdateLocalizations(); QueueScrollToLatest(); ReadingChanged?.Invoke(); return true; }
            var row = rows.FirstOrDefault(r => r.Message.LocalStorageId == id);
            if (row == null) { scrollGeneration++; pendingAnchor = null; followLatest = true; QueueScrollToLatest(); return false; }
            ScrollToMessage(row.Message); return true;
        }

        public event Action? ReadingChanged;

        public ChatMessageList(Tab tab) {
            this.tab = tab;
            this.InitializeComponent();
            foreach (var message in tab.Messages) this.rows.Add(new ChatMessageRow(tab, message));
            this.MessageList.ItemsSource = this.rows;
            this.tab.CollectionChanged += this.OnMessagesChanged;
            this.Loaded += this.OnLoaded;
            this.Unloaded += this.OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (this.disposed) return;
            this.MessageList.ApplyTemplate();
            this.scrollViewer = FindScrollViewer(this.MessageList);
            if (this.scrollViewer != null) {
                this.scrollViewer.ViewChanging += this.OnViewChanging;
                this.scrollViewer.ViewChanged += this.OnViewChanged;
            }
            this.UpdateLocalizations();
            this.QueueScrollToLatest();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            this.DetachScrollViewer();
        }

        private void DetachScrollViewer() {
            if (this.scrollViewer == null) return;
            this.scrollViewer.ViewChanging -= this.OnViewChanging;
            this.scrollViewer.ViewChanged -= this.OnViewChanged;
            this.scrollViewer = null;
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (this.disposed) return;
            // Tab messages are updated on the UI thread. Apply each change in order so
            // pruning indices still match, and coalesce only the expensive scroll/layout work.
            this.updating = true;
            try {
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
                    int index = e.NewStartingIndex < 0 ? this.rows.Count : e.NewStartingIndex;
                    foreach (ServerMessage message in e.NewItems) {
                        this.rows.Insert(index++, new ChatMessageRow(this.tab, message));
                    }
                    // Indexed additions are historical backlog, not new unread chat.
                    if (!this.followLatest && e.NewStartingIndex < 0) this.unreadCount += e.NewItems.Count;
                } else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null) {
                    for (int i = 0; i < e.OldItems.Count; i++) this.rows.RemoveAt(e.OldStartingIndex);
                } else if (e.Action == NotifyCollectionChangedAction.Reset) {
                    this.rows.Clear();
                    foreach (var message in this.tab.Messages) this.rows.Add(new ChatMessageRow(this.tab, message));
                    this.unreadCount = 0;
                    if (this.rows.Count == 0) this.followLatest = true;
                }
                this.UpdateLocalizations();
                this.QueueScrollToLatest();
            } finally {
                this.updating = false;
            }
        }

        private void OnViewChanging(object? sender, ScrollViewerViewChangingEventArgs e) {
            if (this.updating || this.ScrollInProgress || this.scrollViewer == null) return;
            if (e.NextView.VerticalOffset < this.scrollViewer.VerticalOffset - 0.5) {
                this.followLatest = false;
                this.UpdateLocalizations();
            }
        }

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) {
            if (this.updating || this.ScrollInProgress || e.IsIntermediate || this.scrollViewer == null) return;
            if (this.scrollViewer.ScrollableHeight - this.scrollViewer.VerticalOffset <= 2) {
                this.followLatest = true;
                this.unreadCount = 0;
                this.UpdateLocalizations();
            }
            this.ReadingChanged?.Invoke();
        }

        private void QueueScrollToLatest() {
            if (!this.followLatest || this.scrollPending || !this.IsLoaded || this.rows.Count == 0) return;
            this.scrollPending = true;
            var generation = scrollGeneration;
            if (!this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => {
                try {
                    if (this.disposed || generation != scrollGeneration || !this.IsLoaded || !this.followLatest || this.rows.Count == 0) return;
                    this.MessageList.ScrollIntoView(this.rows[this.rows.Count - 1], ScrollIntoViewAlignment.Default);
                    this.MessageList.UpdateLayout();
                    this.scrollViewer?.ChangeView(null, this.scrollViewer.ScrollableHeight, null, true);
                } finally {
                    this.scrollPending = false;
                    if (generation != scrollGeneration && !disposed && followLatest) QueueScrollToLatest();
                }
            })) this.scrollPending = false;
        }

        private void LatestButton_Click(object sender, RoutedEventArgs e) {
            scrollGeneration++;
            pendingAnchor = null;
            this.followLatest = true;
            this.unreadCount = 0;
            this.UpdateLocalizations();
            this.QueueScrollToLatest();
            this.ReadingChanged?.Invoke();
        }

        public void UpdateLocalizations() {
            if (this.MessageList.ItemsPanelRoot is ItemsStackPanel panel) {
                panel.ItemsUpdatingScrollMode = this.followLatest ? ItemsUpdatingScrollMode.KeepLastItemInView : ItemsUpdatingScrollMode.KeepItemsInView;
            }
            this.LatestButton.Content = this.unreadCount > 0
                ? string.Format(CultureInfo.CurrentCulture, LocalizationHelper.GetString("Chat.NewMessages"), this.unreadCount)
                : LocalizationHelper.GetString("Chat.ReturnToLatest");
            this.LatestButton.Visibility = this.followLatest ? Visibility.Collapsed : Visibility.Visible;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent) {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer viewer) return viewer;
                var nested = FindScrollViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }

        public void Dispose() {
            if (this.disposed) return;
            this.disposed = true;
            this.tab.CollectionChanged -= this.OnMessagesChanged;
            this.Loaded -= this.OnLoaded;
            this.Unloaded -= this.OnUnloaded;
            this.DetachScrollViewer();
            this.MessageList.ItemsSource = null;
            this.rows.Clear();
        }
    }

    public sealed class ChatMessageRow {
        public Tab Tab { get; }
        public ServerMessage Message { get; }

        public ChatMessageRow(Tab tab, ServerMessage message) {
            this.Tab = tab;
            this.Message = message;
        }
    }
}
