using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public partial class Export : Window {
        private static readonly HashSet<Export> active = new();
        private readonly App app = (App)Application.Current;
        private readonly HistoryQuery scope;
        private readonly string scopeName;
        private readonly Filter? filter;
        private string? ownerLabel;
        private CancellationTokenSource? operation;
        private Task running = Task.CompletedTask;
        private bool closed, busy;
        private static string L(string key) => LocalizationHelper.GetString(key);
        public Export() : this(new HistoryQuery(Source: ((App)Application.Current).Workbench.Source,
            OwnerKey: ((App)Application.Current).Workbench.OwnerKey), L("Workbench.History")) { }
        public Export(HistoryQuery query, string name, Filter? filter = null) {
            scope = query with { BeforeRow = long.MaxValue, BeforeTimestampUtc = null };
            scopeName = name; this.filter = filter;
            InitializeComponent(); ThemeHelper.InitializeWindow(this);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 760));
            Keyword.Text = query.Text ?? "";
            FromDate.Date = query.FromUtc?.ToLocalTime(); FromTime.Time = query.FromUtc?.ToLocalTime().TimeOfDay ?? TimeSpan.Zero;
            UntilDate.Date = query.UntilUtc?.ToLocalTime(); UntilTime.Time = query.UntilUtc?.ToLocalTime().TimeOfDay ?? TimeSpan.Zero;
            Localize.BindWindow(this, LocalizeWindow);
            active.Add(this);
            Closed += async (_, _) => { closed = true; operation?.Cancel(); try { await running; } catch (Exception) { /* SaveAsync owns the displayed failure; closing only waits for cleanup. */ } finally { active.Remove(this); } };
            Root.Loaded += async (_, _) => {
                try {
                    if (app.Session.Store is { } store && scope.OwnerKey != null) {
                        var owner = (await store.GetOwnersAsync()).FirstOrDefault(o => o.OwnerKey == scope.OwnerKey && (scope.Source == null || o.Source == scope.Source));
                        if (owner?.Identity is { } who) ownerLabel = who.Name + " @ " + who.HomeWorld;
                        if (!closed) LocalizeWindow();
                    }
                } catch (Exception ex) { if (!closed) Status.Text = ex.Message; }
                await PreviewAsync();
            };
        }
        private void LocalizeWindow() {
            Title = L("Export.Title"); Scope.Text = scopeName;
            Explanation.Text = L("Export.DatabaseOnly") + "\n" +
                (scope.OwnerKey == null ? L("History.AllOwners") : ownerLabel ?? L("History.Unassigned")) + " · " + (scope.Source == null ? L("Export.AllConnections") : app.Config.TrustedKeys.FirstOrDefault(k => Convert.ToHexString(k.Key) == scope.Source)?.Name ?? L("Export.SavedConnection")) +
                (scope.Channel is { } channel ? " · " + channel : "") + (scope.Person is { Length: > 0 } person ? " · " + person : "") +
                (scope.BookmarksOnly ? " · " + L("History.BookmarksOnly") : "");
            Keyword.Header = L("Workbench.Search");
            FromDate.PlaceholderText = L("Export.From"); UntilDate.PlaceholderText = L("Export.UntilExclusive");
            Timestamps.Content = L("Export.ShowTimestamps"); PreviewButton.Content = L("Export.Preview");
            ClearDates.Content = L("Export.ClearDates"); SaveButton.Content = L("Dialog.Save"); CancelButton.Content = L("Dialog.Cancel");
            SaveButton.IsEnabled = app.Session.Store != null && !busy;
        }
        internal HistoryQuery Query() {
            var from = FromDate.Date?.Date.Add(FromTime.Time).ToUniversalTime();
            var until = UntilDate.Date?.Date.Add(UntilTime.Time).ToUniversalTime();
            if (from >= until) throw new InvalidOperationException(L("History.InvalidDates"));
            return scope with { Text = Keyword.Text.Trim(), FromUtc = from, UntilUtc = until };
        }
        private void SetBusy(bool value) {
            busy = value;
            SaveButton.IsEnabled = !value && app.Session.Store != null;
            PreviewButton.IsEnabled = ClearDates.IsEnabled = Keyword.IsEnabled = FromDate.IsEnabled = FromTime.IsEnabled = UntilDate.IsEnabled = UntilTime.IsEnabled = Timestamps.IsEnabled = !value;
        }
        private async Task PreviewAsync() {
            if (busy || closed) return;
            if (app.Session.Store is not { } store) { Status.Text = L("History.Unavailable"); return; }
            operation?.Cancel(); operation?.Dispose(); operation = new(); SetBusy(true);
            try {
                var rows = await store.SearchAsync(Query() with { Limit = 500 }, operation.Token);
                if (closed) return;
                PreviewList.ItemsSource = rows.Reverse().Where(r => filter?.Allowed(r.Message) != false).TakeLast(100)
                    .Select(r => HistoryExport.Line(r.Message, Timestamps.IsChecked == true)).ToArray();
                Status.Text = L("Export.PreviewLimit");
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) Status.Text = ex.Message; }
            finally { if (!closed) SetBusy(false); }
        }
        private async void Preview_Click(object sender, RoutedEventArgs e) => await PreviewAsync();
        private void ClearDates_Click(object sender, RoutedEventArgs e) { FromDate.Date = null; UntilDate.Date = null; }
        private void Cancel_Click(object sender, RoutedEventArgs e) { if (busy) operation?.Cancel(); else Close(); }
        private async void Save_Click(object sender, RoutedEventArgs e) {
            if (busy || app.Session.Store == null) return;
            operation?.Dispose(); operation = new();
            await SaveAsync(operation.Token);
        }
        private async Task SaveAsync(CancellationToken token) {
            SetBusy(true);
            try {
                var query = Query(); var store = app.Session.Store!; var timestamps = Timestamps.IsChecked == true;
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "XIVChat Export" };
                picker.FileTypeChoices.Add("Text", new[] { ".txt" }); picker.FileTypeChoices.Add("Rich Text", new[] { ".rtf" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                token.ThrowIfCancellationRequested(); if (file == null) return;
                var progress = new Progress<long>(count => { if (!closed) Status.Text = string.Format(L("Export.Progress"), count); });
                var write = WriteFileAsync(store, file, query, timestamps, filter, progress, token);
                running = write; var count = await write;
                if (!closed) Status.Text = string.Format(L("Export.Complete"), count);
            } catch (OperationCanceledException) { if (!closed) Status.Text = L("Export.Cancelled"); }
            catch (Exception ex) { if (!closed) Status.Text = L("Export.Failed") + " " + ex.Message; }
            finally { if (!closed) SetBusy(false); }
        }
        internal static async Task<long> WriteFileAsync(HistoryStore store, StorageFile file, HistoryQuery query, bool timestamps,
            Filter? filter = null, IProgress<long>? progress = null, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();
            using var transaction = await file.OpenTransactedWriteAsync();
            using var stream = transaction.Stream.AsStreamForWrite(); stream.Position = 0;
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true);
            var count = await store.ExportAsync(query, writer, file.FileType.Equals(".rtf", StringComparison.OrdinalIgnoreCase), timestamps,
                filter == null ? null : filter.Allowed, progress, token);
            await writer.FlushAsync(token); token.ThrowIfCancellationRequested();
            stream.SetLength(stream.Position); await transaction.CommitAsync(); return count;
        }
        public static async Task CancelAllAsync() {
            var windows = active.ToArray(); foreach (var window in windows) window.operation?.Cancel();
            await Task.WhenAll(windows.Select(async w => { try { await w.running; } catch (Exception) { /* The export window has already reported this failure. */ } }));
        }
    }
}
