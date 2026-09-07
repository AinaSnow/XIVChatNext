using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
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
        private bool updating;
        private bool disposed;
        private int unreadCount;

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
            if (this.updating || this.scrollPending || this.scrollViewer == null) return;
            if (e.NextView.VerticalOffset < this.scrollViewer.VerticalOffset - 0.5) {
                this.followLatest = false;
                this.UpdateLocalizations();
            }
        }

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) {
            if (this.updating || this.scrollPending || e.IsIntermediate || this.scrollViewer == null) return;
            if (this.scrollViewer.ScrollableHeight - this.scrollViewer.VerticalOffset <= 2) {
                this.followLatest = true;
                this.unreadCount = 0;
                this.UpdateLocalizations();
            }
        }

        private void QueueScrollToLatest() {
            if (!this.followLatest || this.scrollPending || !this.IsLoaded || this.rows.Count == 0) return;
            this.scrollPending = true;
            if (!this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => {
                try {
                    if (this.disposed || !this.IsLoaded || !this.followLatest || this.rows.Count == 0) return;
                    this.MessageList.ScrollIntoView(this.rows[this.rows.Count - 1], ScrollIntoViewAlignment.Default);
                    this.MessageList.UpdateLayout();
                    this.scrollViewer?.ChangeView(null, this.scrollViewer.ScrollableHeight, null, true);
                } finally {
                    this.scrollPending = false;
                }
            })) this.scrollPending = false;
        }

        private void LatestButton_Click(object sender, RoutedEventArgs e) {
            this.followLatest = true;
            this.unreadCount = 0;
            this.UpdateLocalizations();
            this.QueueScrollToLatest();
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
