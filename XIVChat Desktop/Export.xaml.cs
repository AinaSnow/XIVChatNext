using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            app.Presentation.PolicyChanged += PrivacyChanged;
            Closed += async (_, _) => { closed = true; app.Presentation.PolicyChanged -= PrivacyChanged; operation?.Cancel(); try { await running; } catch (Exception) { /* SaveAsync owns the displayed failure; closing only waits for cleanup. */ } finally { active.Remove(this); } };
            Root.Loaded += async (_, _) => {
                try {
                    if (app.Session.Store is { } store && scope.OwnerKey != null) {
                        var owner = (await store.GetOwnersAsync()).FirstOrDefault(o => o.OwnerKey == scope.OwnerKey && (scope.Source == null || o.Source == scope.Source));
                        if (owner?.Identity is { } who) { app.Presentation.Context(owner.Source, owner.OwnerKey, who); ownerLabel = who.Name + " @ " + who.HomeWorld; }
                        if (!closed) LocalizeWindow();
                    }
                } catch (Exception ex) { if (!closed) Status.Text = ex.Message; }
                await PreviewAsync();
            };
        }
        private void LocalizeWindow() {
            Title = L("Export.Title"); Scope.Text = app.Presentation.Text(scopeName, app.Presentation.Context(scope.Source ?? "", scope.OwnerKey));
            Explanation.Text = L("Export.DatabaseOnly") + "\n" +
                (scope.OwnerKey == null ? L("History.AllOwners") : ownerLabel ?? L("History.Unassigned")) + " · " + (scope.Source == null ? L("Export.AllConnections") : app.Config.TrustedKeys.FirstOrDefault(k => Convert.ToHexString(k.Key) == scope.Source)?.Name ?? L("Export.SavedConnection")) +
                (scope.Channel is { } channel ? " · " + channel : "") + (scope.Person is { Length: > 0 } person ? " · " + person : "") +
                (scope.BookmarksOnly ? " · " + L("History.BookmarksOnly") : "");
            Explanation.Text = app.Presentation.Text(Explanation.Text, app.Presentation.Context(scope.Source ?? "", scope.OwnerKey)) +
                (app.Presentation.Enabled ? "\n" + L("Privacy.Export") : "");
            Keyword.Header = L("Workbench.Search");
            FromDate.PlaceholderText = L("Export.From"); UntilDate.PlaceholderText = L("Export.UntilExclusive");
            Timestamps.Content = L("Export.ShowTimestamps"); PreviewButton.Content = L("Export.Preview");
            ClearDates.Content = L("Export.ClearDates"); SaveButton.Content = L("Dialog.Save"); CancelButton.Content = L("Dialog.Cancel");
            AiSummaryButton.Content = L("Ai.Summarize"); AiIncludeSummary.Content = L("Ai.IncludeSummary");
            AiSummaryButton.Visibility = AiIncludeSummary.Visibility = app.Ai.Enabled ? Visibility.Visible : Visibility.Collapsed;
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
            AiSummaryButton.IsEnabled = AiIncludeSummary.IsEnabled = !value;
            PreviewButton.IsEnabled = ClearDates.IsEnabled = Keyword.IsEnabled = FromDate.IsEnabled = FromTime.IsEnabled = UntilDate.IsEnabled = UntilTime.IsEnabled = Timestamps.IsEnabled = !value;
        }
        private async Task PreviewAsync() {
            if (busy || closed) return;
            if (app.Session.Store is not { } store) { Status.Text = L("History.Unavailable"); return; }
            operation?.Cancel(); operation?.Dispose(); operation = new(); SetBusy(true);
            try {
                var rows = await store.SearchAsync(Query() with { Limit = 500 }, operation.Token);
                if (closed || operation.IsCancellationRequested) return;
                foreach (var row in rows) app.Presentation.Observe(row.Message);
                var display = app.Presentation.Engine.Snapshot();
                PreviewList.ItemsSource = rows.Reverse().Where(r => filter?.Allowed(r.Message) != false).TakeLast(100)
                    .Select(r => display.ExportLine(r.Source, r.Message, Timestamps.IsChecked == true, ChannelLabels.For(r.Message, !display.Enabled))).ToArray();
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
                picker.FileTypeChoices.Add("Text", new[] { ".txt" }); picker.FileTypeChoices.Add("Markdown", new[] { ".md" }); picker.FileTypeChoices.Add("Rich Text", new[] { ".rtf" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                token.ThrowIfCancellationRequested(); if (file == null) return;
                string? preface = null;
                if (AiIncludeSummary.IsChecked == true && app.Ai.Enabled) {
                    if (HistoryExport.FormatForExtension(file.FileType) != ExportFormat.Markdown) { Status.Text = L("Ai.SummaryMarkdownOnly"); return; }
                    Status.Text = L("Ai.Summarizing");
                    preface = SummarySection(await app.Ai.SummarizeAsync(store, query, filter == null ? null : filter.Allowed, SummaryScope(query), token));
                }
                var progress = new Progress<long>(count => { if (!closed) Status.Text = string.Format(L("Export.Progress"), count); });
                var display = app.Presentation.Engine.Snapshot();
                var write = WriteFileAsync(store, file, query, timestamps, filter, progress, token, (row, showTime) => display.ExportLine(row.Source, row.Message, showTime, ChannelLabels.For(row.Message, !display.Enabled)),
                    display.Enabled ? row => display.Observe(row.Source, row.Message) : null, Scope.Text, preface);
                running = write; var count = await write;
                if (!closed) Status.Text = string.Format(L("Export.Complete"), count);
            } catch (OperationCanceledException) { if (!closed) Status.Text = L("Export.Cancelled"); }
            catch (XIVChatAi.AiException ex) { if (!closed) Status.Text = L("Ai.SummaryFailedNotSaved") + " " + AiAssistant.Describe(ex); }
            catch (Exception ex) { if (!closed) Status.Text = L("Export.Failed") + " " + ex.Message; }
            finally { if (!closed) SetBusy(false); }
        }
        internal static async Task<long> WriteFileAsync(HistoryStore store, StorageFile file, HistoryQuery query, bool timestamps,
            Filter? filter = null, IProgress<long>? progress = null, CancellationToken token = default,
            Func<HistoryRow, bool, string>? displayLine = null, Action<HistoryRow>? prepareRow = null, string? title = null, string? preface = null) {
            token.ThrowIfCancellationRequested();
            using var transaction = await file.OpenTransactedWriteAsync();
            using var stream = transaction.Stream.AsStreamForWrite(); stream.Position = 0;
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true);
            var count = await store.ExportAsync(query, writer, HistoryExport.FormatForExtension(file.FileType), timestamps,
                filter == null ? null : filter.Allowed, progress, token, displayLine, prepareRow, title, preface);
            await writer.FlushAsync(token); token.ThrowIfCancellationRequested();
            stream.SetLength(stream.Position); await transaction.CommitAsync(); return count;
        }
        internal string SummaryScope(HistoryQuery query) =>
            Scope.Text + (query.FromUtc is { } from ? " · " + from.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "") +
            (query.UntilUtc is { } until ? " – " + until.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "") +
            (string.IsNullOrWhiteSpace(query.Text) ? "" : " · " + query.Text);

        internal string SummarySection(string summary) =>
            "## " + L("Ai.SummaryHeading") + "\n\n> " + string.Format(L("Ai.SummaryCaveat"), app.Ai.Settings().Model) + "\n\n" + summary.Trim() + "\n";

        private async void AiSummary_Click(object sender, RoutedEventArgs e) {
            if (busy || app.Session.Store is not { } store) return;
            operation?.Dispose(); operation = new(); SetBusy(true);
            Status.Text = L("Ai.Summarizing");
            string? summary = null;
            try {
                var query = Query();
                summary = await app.Ai.SummarizeAsync(store, query, filter == null ? null : filter.Allowed, SummaryScope(query), operation.Token);
                if (!closed) Status.Text = "";
            } catch (OperationCanceledException) { if (!closed) Status.Text = L("Export.Cancelled"); }
            catch (XIVChatAi.AiException ex) { if (!closed) Status.Text = AiAssistant.Describe(ex); }
            catch (Exception ex) { if (!closed) Status.Text = L("Ai.Failed") + " " + ex.Message; }
            finally { if (!closed) SetBusy(false); }
            if (summary == null || closed) return;
            var text = new TextBox { Text = summary, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 320, MaxHeight = 520 };
            ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
            var dialog = new ContentDialog {
                Title = L("Ai.SummaryHeading"), Content = text, XamlRoot = Root.XamlRoot,
                PrimaryButtonText = L("Ai.Copy"), CloseButtonText = L("Dialog.Close"),
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(summary);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            }
        }

        public static async Task CancelAllAsync() {
            var windows = active.ToArray(); foreach (var window in windows) window.operation?.Cancel();
            await Task.WhenAll(windows.Select(async w => { try { await w.running; } catch (Exception) { /* The export window has already reported this failure. */ } }));
        }
        private void PrivacyChanged() {
            operation?.Cancel(); PreviewList.ItemsSource = null; LocalizeWindow(); Status.Text = L("Privacy.ExportChanged");
        }
    }
}
