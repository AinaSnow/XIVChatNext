using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.Text;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public class MessageFormatter {
        private static readonly FontFamily FfxivFontFamily = new FontFamily("ms-appx:///Resources/fonts/ffxiv.ttf#XIV AXIS Std ATK");
        private static byte[]? _atlasPixels;
        private static int _atlasWidth;
        private static int _atlasHeight;
        private static readonly Dictionary<byte, WriteableBitmap> _croppedIcons = new Dictionary<byte, WriteableBitmap>();

        private static void ApplyFontFamily(Inline inline) {
            if (inline is Run run) {
                run.FontFamily = FfxivFontFamily;
            } else if (inline is Span span) {
                span.FontFamily = FfxivFontFamily;
                foreach (var child in span.Inlines) {
                    ApplyFontFamily(child);
                }
            }
        }

        private static ImageSource? GetCroppedIcon(byte id, Windows.Foundation.Rect bounds) {
            try {
                if (_croppedIcons.TryGetValue(id, out var cached)) {
                    return cached;
                }

                if (_atlasPixels == null) {
                    var path = Path.Combine(AppContext.BaseDirectory, "Resources", "fonticon_ps4.tex.png");
                    if (!File.Exists(path)) {
                        return null;
                    }
                    using var stream = File.OpenRead(path);
                    var ras = stream.AsRandomAccessStream();
                    var decoderTask = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask();
                    decoderTask.Wait();
                    var decoder = decoderTask.Result;
                    _atlasWidth = (int)decoder.PixelWidth;
                    _atlasHeight = (int)decoder.PixelHeight;

                    var pixelDataTask = decoder.GetPixelDataAsync(
                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                        Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                        new Windows.Graphics.Imaging.BitmapTransform(),
                        Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                        Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage
                    ).AsTask();
                    pixelDataTask.Wait();
                    _atlasPixels = pixelDataTask.Result.DetachPixelData();
                }

                int x = (int)bounds.X;
                int y = (int)bounds.Y;
                int w = (int)bounds.Width;
                int h = (int)bounds.Height;

                if (x + w > _atlasWidth || y + h > _atlasHeight || w <= 0 || h <= 0) {
                    return null;
                }

                var wb = new WriteableBitmap(w, h);
                using (var destStream = wb.PixelBuffer.AsStream()) {
                    var destBytes = new byte[w * h * 4];
                    for (int row = 0; row < h; row++) {
                        int srcIdx = ((y + row) * _atlasWidth + x) * 4;
                        int destIdx = row * w * 4;
                        Array.Copy(_atlasPixels, srcIdx, destBytes, destIdx, w * 4);
                    }
                    destStream.Write(destBytes, 0, destBytes.Length);
                }

                _croppedIcons[id] = wb;
                return wb;
            } catch {
                return null;
            }
        }

        public static IEnumerable<Inline> ChunksToTextBlock(ServerMessage message, double fontSize, bool processMarkdown, bool showTimestamp) {
            var elements = new List<Inline>();

            if (showTimestamp) {
                var timestampString = message.Timestamp.ToLocalTime().ToString("t", CultureInfo.CurrentUICulture);
                var tsRun = new Run {
                    Text = $"[{timestampString}]",
                    Foreground = new SolidColorBrush(Colors.White),
                };
                ApplyFontFamily(tsRun);
                elements.Add(tsRun);
            }

            foreach (var chunk in message.Chunks) {
                switch (chunk) {
                    case TextChunk textChunk:
                        var colour = textChunk.Foreground ?? textChunk.FallbackColour ?? 0;

                        SolidColorBrush brush;
                        if (colour == 0) {
                            brush = new SolidColorBrush(Colors.White);
                        } else {
                            var r = (byte)((colour >> 24) & 0xFF);
                            var g = (byte)((colour >> 16) & 0xFF);
                            var b = (byte)((colour >> 8) & 0xFF);
                            var a = (byte)(colour & 0xFF);
                            if (a == 0) a = 255;
                            brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                        }
                        var style = textChunk.Italic ? FontStyle.Italic : FontStyle.Normal;

                        bool isMapLink = textChunk.MapId.HasValue && textChunk.MapId.Value > 0;
                        System.Text.RegularExpressions.Match? coordMatch = null;
                        var coordRegex = @"[\(\[\{（【]?\s*([Xx]:?\s*)?([0-9]+(?:\.[0-9]+)?)\s*[,，]\s*([Yy]:?\s*)?([0-9]+(?:\.[0-9]+)?)\s*[\)\]\}）】]?";
                        if (!isMapLink && !string.IsNullOrEmpty(textChunk.Content)) {
                            coordMatch = System.Text.RegularExpressions.Regex.Match(textChunk.Content, coordRegex);
                            if (coordMatch.Success) {
                                isMapLink = true;
                            }
                        }

                        if (isMapLink) {
                            var linkBrush = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));
                            var linkRun = new Run {
                                Text = textChunk.Content,
                                Foreground = linkBrush,
                                FontStyle = style,
                            };
                            ApplyFontFamily(linkRun);

                            var hyperlink = new Hyperlink();
                            hyperlink.UnderlineStyle = UnderlineStyle.Single;
                            hyperlink.Inlines.Add(linkRun);

                            uint targetMapId = textChunk.MapId ?? 0;
                            float targetX = 0;
                            float targetY = 0;
                            if (coordMatch != null && coordMatch.Success) {
                                float.TryParse(coordMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetX);
                                float.TryParse(coordMatch.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetY);
                            }
                            if ((targetX <= 0 || targetY <= 0) && !string.IsNullOrEmpty(textChunk.Content)) {
                                var m2 = System.Text.RegularExpressions.Regex.Match(textChunk.Content, coordRegex);
                                if (m2.Success) {
                                    float.TryParse(m2.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetX);
                                    float.TryParse(m2.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetY);
                                }
                            }
                            if (targetX <= 0 || targetY <= 0) {
                                targetX = textChunk.MapX ?? 0;
                                targetY = textChunk.MapY ?? 0;
                            }
                            string targetName = !string.IsNullOrEmpty(textChunk.MapPlaceName) ? textChunk.MapPlaceName : textChunk.Content;
                            string? targetFilenameId = textChunk.MapFilenameId;
                            ushort? targetSizeFactor = textChunk.MapSizeFactor;

                            hyperlink.Click += (s, args) => {
                                MapWindow.ShowMap(targetMapId > 0 ? targetMapId : null, targetX > 0 ? targetX : null, targetY > 0 ? targetY : null, targetName, targetFilenameId, targetSizeFactor);
                            };

                            elements.Add(hyperlink);
                            break;
                        }

                        bool isItemLink = textChunk.ItemId.HasValue && textChunk.ItemId.Value > 0;
                        if (!isItemLink && !string.IsNullOrEmpty(textChunk.Content)) {
                            if (System.Text.RegularExpressions.Regex.IsMatch(textChunk.Content.TrimStart(), @"^[]")) {
                                isItemLink = true;
                            }
                        }

                        if (isItemLink) {
                            Color linkColor = textChunk.ItemRarity switch {
                                1 => Color.FromArgb(255, 240, 240, 240), // 白装/普通物品
                                2 => Color.FromArgb(255, 140, 230, 140), // 绿装
                                3 => Color.FromArgb(255, 89, 144, 255),  // 蓝装
                                4 => Color.FromArgb(255, 184, 128, 255), // 紫装
                                7 => Color.FromArgb(255, 255, 115, 190), // 粉装
                                _ => Color.FromArgb(255, 255, 205, 90)   // 默认金色
                            };
                            var linkBrush = new SolidColorBrush(linkColor);
                            var linkRun = new Run {
                                Text = textChunk.Content,
                                Foreground = linkBrush,
                                FontStyle = style,
                            };
                            ApplyFontFamily(linkRun);

                            var hyperlink = new Hyperlink();
                            hyperlink.UnderlineStyle = UnderlineStyle.Single;
                            hyperlink.Inlines.Add(linkRun);

                            uint? targetItemId = textChunk.ItemId;
                            bool targetIsHq = textChunk.IsHq ?? (textChunk.Content != null && (textChunk.Content.Contains("") || textChunk.Content.Contains("HQ", StringComparison.OrdinalIgnoreCase)));
                            string targetName = textChunk.Content ?? "";

                            var passedChunk = textChunk;
                            hyperlink.Click += (s, args) => {
                                ItemWindow.ShowItem(targetItemId > 0 ? targetItemId : null, targetIsHq, targetName, passedChunk);
                            };

                            elements.Add(hyperlink);
                            break;
                        }

                        if (processMarkdown) {
                            var inlines = Markdown.MarkdownToInlines(textChunk.Content);

                            foreach (var inline in inlines) {
                                if (inline is Run run) {
                                    run.Foreground = brush;
                                    if (run.FontStyle == FontStyle.Normal) {
                                        run.FontStyle = style;
                                    }
                                }
                                ApplyFontFamily(inline);
                                elements.Add(inline);
                            }
                        } else {
                            var run = new Run {
                                Text = textChunk.Content,
                                Foreground = brush,
                                FontStyle = style,
                            };
                            ApplyFontFamily(run);
                            elements.Add(run);
                        }

                        break;
                    case IconChunk iconChunk:
                        var bounds = GetBounds(iconChunk.index);
                        if (bounds == null) {
                            break;
                        }

                        var lineHeight = fontSize * 1.2;
                        var width = lineHeight / bounds.Value.Height * bounds.Value.Width;

                        var iconSource = GetCroppedIcon(iconChunk.index, bounds.Value);
                        if (iconSource != null) {
                            var image = new Image {
                                Source = iconSource,
                                Width = width,
                                Height = lineHeight,
                            };
                            elements.Add(new InlineUIContainer {
                                Child = image,
                            });
                        }
                        break;
                }
            }

            return elements;
        }

        private static Windows.Foundation.Rect? GetBounds(byte id) => id switch {
            1 => new Windows.Foundation.Rect(0, 0, 20, 20),
            2 => new Windows.Foundation.Rect(20, 0, 20, 20),
            3 => new Windows.Foundation.Rect(40, 0, 20, 20),
            4 => new Windows.Foundation.Rect(60, 0, 20, 20),
            5 => new Windows.Foundation.Rect(80, 0, 20, 20),
            6 => new Windows.Foundation.Rect(0, 20, 20, 20),
            7 => new Windows.Foundation.Rect(20, 20, 20, 20),
            8 => new Windows.Foundation.Rect(40, 20, 20, 20),
            9 => new Windows.Foundation.Rect(60, 20, 20, 20),
            10 => new Windows.Foundation.Rect(80, 20, 20, 20),
            11 => new Windows.Foundation.Rect(0, 40, 20, 20),
            12 => new Windows.Foundation.Rect(20, 40, 20, 20),
            13 => new Windows.Foundation.Rect(40, 40, 20, 20),
            14 => new Windows.Foundation.Rect(60, 40, 20, 20),
            15 => new Windows.Foundation.Rect(80, 40, 20, 20),
            16 => new Windows.Foundation.Rect(60, 100, 20, 20),
            17 => new Windows.Foundation.Rect(80, 100, 20, 20),
            18 => new Windows.Foundation.Rect(0, 60, 54, 20),
            19 => new Windows.Foundation.Rect(54, 60, 54, 20),
            20 => new Windows.Foundation.Rect(60, 80, 20, 20),
            21 => new Windows.Foundation.Rect(0, 80, 28, 20),
            22 => new Windows.Foundation.Rect(28, 80, 32, 20),
            23 => new Windows.Foundation.Rect(80, 80, 20, 20),
            24 => new Windows.Foundation.Rect(0, 100, 28, 20),
            25 => new Windows.Foundation.Rect(28, 100, 32, 20),
            51 => new Windows.Foundation.Rect(124, 0, 20, 20),
            52 => new Windows.Foundation.Rect(144, 0, 20, 20),
            53 => new Windows.Foundation.Rect(164, 0, 20, 20),
            54 => new Windows.Foundation.Rect(100, 0, 12, 20),
            55 => new Windows.Foundation.Rect(112, 0, 12, 20),
            56 => new Windows.Foundation.Rect(100, 20, 20, 20),
            57 => new Windows.Foundation.Rect(120, 20, 20, 20),
            58 => new Windows.Foundation.Rect(140, 20, 20, 20),
            59 => new Windows.Foundation.Rect(100, 40, 20, 20),
            60 => new Windows.Foundation.Rect(120, 40, 20, 20),
            61 => new Windows.Foundation.Rect(140, 40, 20, 20),
            62 => new Windows.Foundation.Rect(160, 20, 20, 20),
            63 => new Windows.Foundation.Rect(160, 40, 20, 20),
            64 => new Windows.Foundation.Rect(184, 0, 20, 20),
            65 => new Windows.Foundation.Rect(204, 0, 20, 20),
            66 => new Windows.Foundation.Rect(224, 0, 20, 20),
            67 => new Windows.Foundation.Rect(180, 20, 20, 20),
            68 => new Windows.Foundation.Rect(200, 20, 20, 20),
            69 => new Windows.Foundation.Rect(236, 236, 20, 20),
            70 => new Windows.Foundation.Rect(180, 40, 20, 20),
            71 => new Windows.Foundation.Rect(200, 40, 20, 20),
            72 => new Windows.Foundation.Rect(220, 40, 20, 20),
            73 => new Windows.Foundation.Rect(220, 20, 20, 20),
            74 => new Windows.Foundation.Rect(108, 60, 20, 20),
            75 => new Windows.Foundation.Rect(128, 60, 20, 20),
            76 => new Windows.Foundation.Rect(148, 60, 20, 20),
            77 => new Windows.Foundation.Rect(168, 60, 20, 20),
            78 => new Windows.Foundation.Rect(188, 60, 20, 20),
            79 => new Windows.Foundation.Rect(208, 60, 20, 20),
            80 => new Windows.Foundation.Rect(228, 60, 20, 20),
            81 => new Windows.Foundation.Rect(100, 80, 20, 20),
            82 => new Windows.Foundation.Rect(120, 80, 20, 20),
            83 => new Windows.Foundation.Rect(140, 80, 20, 20),
            84 => new Windows.Foundation.Rect(160, 80, 20, 20),
            85 => new Windows.Foundation.Rect(180, 80, 20, 20),
            86 => new Windows.Foundation.Rect(200, 80, 20, 20),
            87 => new Windows.Foundation.Rect(220, 80, 20, 20),
            88 => new Windows.Foundation.Rect(100, 100, 20, 20),
            89 => new Windows.Foundation.Rect(120, 100, 20, 20),
            90 => new Windows.Foundation.Rect(140, 100, 20, 20),
            91 => new Windows.Foundation.Rect(160, 100, 20, 20),
            92 => new Windows.Foundation.Rect(180, 100, 20, 20),
            93 => new Windows.Foundation.Rect(200, 100, 20, 20),
            94 => new Windows.Foundation.Rect(220, 100, 20, 20),
            95 => new Windows.Foundation.Rect(0, 120, 20, 20),
            96 => new Windows.Foundation.Rect(20, 120, 20, 20),
            97 => new Windows.Foundation.Rect(40, 120, 20, 20),
            98 => new Windows.Foundation.Rect(60, 120, 20, 20),
            99 => new Windows.Foundation.Rect(80, 120, 20, 20),
            100 => new Windows.Foundation.Rect(100, 120, 20, 20),
            101 => new Windows.Foundation.Rect(120, 120, 20, 20),
            102 => new Windows.Foundation.Rect(140, 120, 20, 20),
            103 => new Windows.Foundation.Rect(160, 120, 20, 20),
            104 => new Windows.Foundation.Rect(180, 120, 20, 20),
            105 => new Windows.Foundation.Rect(200, 120, 20, 20),
            106 => new Windows.Foundation.Rect(220, 120, 20, 20),
            107 => new Windows.Foundation.Rect(0, 140, 20, 20),
            108 => new Windows.Foundation.Rect(20, 140, 20, 20),
            109 => new Windows.Foundation.Rect(40, 140, 20, 20),
            110 => new Windows.Foundation.Rect(60, 140, 20, 20),
            111 => new Windows.Foundation.Rect(80, 140, 20, 20),
            _ => null,
        };
    }
}
