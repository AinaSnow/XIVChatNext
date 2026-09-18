using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using XIVChatCommon;

namespace XIVChat_Desktop {
    public sealed partial class ScreenshotWindow : Window {
        private static ScreenshotWindow? instance;
        private App App => (App)Application.Current;
        private ScreenshotSession Session => this.App.Screenshots;
        private ScreenshotPreview? displayed;
        private ScreenshotPreview? loading;
        private bool closed, fitting = true, saving;
        private int imageVersion;
        public static ScreenshotWindow ShowScreenshot() {
            instance ??= new ScreenshotWindow(); instance.Activate(); return instance;
        }
        internal static void CloseActive() => instance?.Close();
        public ScreenshotWindow() {
            this.InitializeComponent(); Branding.ApplyWindowIcon(this);
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 800));
            this.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
            this.Session.Changed += this.Update;
            this.App.Presentation.Changed += this.Update;
            this.App.PropertyChanged += this.AppChanged;
            this.Root.Loaded += (_, _) => this.Update();
            Localize.BindWindow(this, this.LocalizeWindow);
            this.Closed += (_, _) => {
                this.closed = true; this.imageVersion++; this.Session.Changed -= this.Update;
                this.App.Presentation.Changed -= this.Update;
                this.App.PropertyChanged -= this.AppChanged; this.Session.Close();
                this.PreviewImage.Source = null; this.displayed = null; this.loading = null; instance = null;
            };
            this.Session.UpdateContext();
        }
        private static string L(string key) => LocalizationHelper.GetString(key);
        private void LocalizeWindow() {
            this.Title = "XIVChat · " + L("Screenshot.Title");
            // WinUI caches the collapsed selection text when a ComboBoxItem is selected.
            int selectedQuality = this.QualityPicker.SelectedIndex;
            this.QualityPicker.SelectedIndex = -1;
            this.StandardOption.Content = L("Screenshot.Standard"); this.DetailedOption.Content = L("Screenshot.Detailed");
            this.QualityPicker.SelectedIndex = selectedQuality;
            this.CaptureButton.Content = L("Screenshot.Capture"); this.CancelButton.Content = L("Screenshot.Cancel");
            this.SaveButton.Content = L("Screenshot.Save"); this.FitButton.Content = L("Screenshot.Fit");
            this.EmptyText.Text = L("Screenshot.Empty");
            ToolTipService.SetToolTip(this.ZoomInButton, L("Screenshot.ZoomIn")); ToolTipService.SetToolTip(this.ZoomOutButton, L("Screenshot.ZoomOut"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this.QualityPicker, L("Screenshot.Quality"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this.PreviewImage, L("Screenshot.Title"));
            this.Update();
        }
        private void AppChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(App.Connection)) this.Update(); }
        private void Update() {
            if (this.closed) return;
            this.CaptureButton.IsEnabled = this.Session.CanCapture && !this.Session.Busy && !this.saving;
            this.CancelButton.IsEnabled = this.Session.Busy;
            this.QualityPicker.IsEnabled = !this.Session.Busy && !this.saving;
            this.SaveButton.IsEnabled = this.displayed != null && !this.saving && !this.Session.Busy;
            this.TransferProgress.Visibility = this.Session.Busy ? Visibility.Visible : Visibility.Collapsed;
            this.TransferProgress.IsIndeterminate = this.Session.Progress.Total == 0;
            this.TransferProgress.Value = this.Session.Progress.Total > 0 ? 100d * this.Session.Progress.Received / this.Session.Progress.Total : 0;
            this.StateText.Text = this.Session.StateKey == "Screenshot.Receiving"
                ? string.Format(L("Screenshot.Receiving"), (int)this.TransferProgress.Value) : L(this.Session.StateKey);
            if (!this.Session.Busy && !this.Session.CanCapture)
                this.StateText.Text = L(this.App.Connection?.Available == true && this.App.Connection.SupportsScreenshots == false ? "Screenshot.UpgradeRequired" : "Screenshot.ConnectRequired");
            if (this.Session.Preview is { } preview && !ReferenceEquals(this.displayed, preview) && !ReferenceEquals(this.loading, preview)) _ = this.LoadImageAsync(preview);
            bool hasImage = this.displayed != null;
            this.EmptyPanel.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
            this.FitButton.IsEnabled = this.ActualButton.IsEnabled = this.ZoomInButton.IsEnabled = this.ZoomOutButton.IsEnabled = hasImage;
            this.SourceText.Text = this.displayed is { } shown ? this.App.Presentation.OwnerLabel(shown.Source, shown.OwnerKey) + (this.Session.IsCurrent(shown) ? "" : " · " + L("Screenshot.Previous")) : "";
            this.DetailsText.Text = this.displayed is { } details
                ? string.Format(L("Screenshot.Details"), DateTimeOffset.FromUnixTimeMilliseconds(details.Image.CapturedAtUnixMilliseconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    details.Image.Width, details.Image.Height, (details.Image.Bytes.Length / 1024d).ToString("N0")) : L("Screenshot.Hint");
            this.DetailsText.Text += "\n" + L("Privacy.Screenshot");
        }
        private async Task LoadImageAsync(ScreenshotPreview preview) {
            this.loading = preview;
            int version = ++this.imageVersion;
            try {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream)) { writer.WriteBytes(preview.Image.Bytes); await writer.StoreAsync(); writer.DetachStream(); }
                stream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.JpegDecoderId, stream);
                if (decoder.PixelWidth != preview.Image.Width || decoder.PixelHeight != preview.Image.Height) throw new InvalidDataException("Unexpected image dimensions.");
                stream.Seek(0);
                var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
                if (this.closed || version != this.imageVersion || !ReferenceEquals(this.Session.Preview, preview)) return;
                this.displayed = preview; this.PreviewImage.Source = bitmap;
                this.PreviewImage.Width = preview.Image.Width; this.PreviewImage.Height = preview.Image.Height;
                this.PreviewImage.UpdateLayout(); this.fitting = true; this.Fit(); this.Update();
            } catch { if (!this.closed && version == this.imageVersion) this.StateText.Text = L("Screenshot.Unavailable"); }
        }
        private async void Capture_Click(object sender, RoutedEventArgs e) => await this.Session.CaptureAsync(this.QualityPicker.SelectedIndex == 1 ? ScreenshotQuality.Detailed : ScreenshotQuality.Standard);
        private void Cancel_Click(object sender, RoutedEventArgs e) => this.Session.Cancel();
        private void Fit_Click(object sender, RoutedEventArgs e) { this.fitting = true; this.Fit(); }
        private void Actual_Click(object sender, RoutedEventArgs e) { this.fitting = false; this.Viewport.ChangeView(0, 0, 1f, true); }
        private void ZoomIn_Click(object sender, RoutedEventArgs e) { this.fitting = false; this.Viewport.ChangeView(null, null, Math.Min(4f, this.Viewport.ZoomFactor * 1.25f), true); }
        private void ZoomOut_Click(object sender, RoutedEventArgs e) { this.fitting = false; this.Viewport.ChangeView(null, null, Math.Max(.1f, this.Viewport.ZoomFactor / 1.25f), true); }
        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (this.fitting) this.Fit(); }
        private void Fit() {
            if (this.displayed == null || this.Viewport.ActualWidth <= 0 || this.Viewport.ActualHeight <= 0) return;
            var image = this.displayed.Image;
            this.Viewport.ChangeView(0, 0, (float)Math.Clamp(Math.Min(this.Viewport.ActualWidth / image.Width, this.Viewport.ActualHeight / image.Height), .1, 1), true);
        }
        private async void Save_Click(object sender, RoutedEventArgs e) {
            var preview = this.displayed;
            if (preview == null || this.saving) return;
            this.saving = true; this.Update();
            try {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = "XIVChat-" + DateTimeOffset.FromUnixTimeMilliseconds(preview.Image.CapturedAtUnixMilliseconds).ToLocalTime().ToString("yyyyMMdd-HHmmss") };
                picker.FileTypeChoices.Add("JPEG", new List<string> { ".jpg" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file == null || this.closed) return;
                await SaveImageAsync(preview.Image, file);
                if (!this.closed) this.StateText.Text = L("Screenshot.Saved");
            } catch { if (!this.closed) this.StateText.Text = L("Screenshot.SaveFailed"); }
            finally {
                this.saving = false;
                if (!this.closed) {
                    this.SaveButton.IsEnabled = this.displayed != null && !this.Session.Busy;
                    this.CaptureButton.IsEnabled = this.Session.CanCapture && !this.Session.Busy;
                    this.QualityPicker.IsEnabled = !this.Session.Busy;
                }
            }
        }
        internal static async Task SaveImageAsync(ScreenshotImage image, StorageFile file) {
            // Transactional writes preserve an existing file when a write fails or is interrupted.
            using var transaction = await file.OpenTransactedWriteAsync();
            transaction.Stream.Size = 0;
            using (var writer = new DataWriter(transaction.Stream)) { writer.WriteBytes(image.Bytes); await writer.StoreAsync(); writer.DetachStream(); }
            await transaction.CommitAsync();
        }
    }
}
