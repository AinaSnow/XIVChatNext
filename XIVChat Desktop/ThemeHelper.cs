using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace XIVChat_Desktop {
    public static class ThemeHelper {
        private static readonly HashSet<Window> ActiveWindows = new HashSet<Window>();

        public static Microsoft.UI.Xaml.Controls.Grid CreateSurface() => (Microsoft.UI.Xaml.Controls.Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource ApplicationPageBackgroundThemeBrush}'/>");

        public static void InitializeWindow(Window window) {
            Branding.ApplyWindowIcon(window);
            lock (ActiveWindows) {
                ActiveWindows.Add(window);
            }
            window.Closed += (s, e) => {
                lock (ActiveWindows) {
                    ActiveWindows.Remove(window);
                }
            };

            // Enable Windows 11 Mica / DesktopAcrylic System Backdrop
            try {
                if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) {
                    window.SystemBackdrop = new MicaBackdrop();
                } else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()) {
                    window.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
            } catch { }

            ApplyCurrentThemeToWindow(window);
        }

        internal static void CloseAuxiliaryWindows(Window main) {
            Window[] snapshot;
            lock (ActiveWindows) snapshot = ActiveWindows.Where(w => !ReferenceEquals(w, main)).ToArray();
            foreach (var window in snapshot) window.Close();
        }

        public static void ApplyTheme(Theme theme) {
            var elementTheme = theme switch {
                Theme.Light => ElementTheme.Light,
                Theme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            lock (ActiveWindows) {
                foreach (var window in ActiveWindows) {
                    if (window.Content is FrameworkElement fe) {
                        fe.RequestedTheme = elementTheme;
                    }
                }
            }
        }

        private static void ApplyCurrentThemeToWindow(Window window) {
            var config = ((App)Application.Current)?.Config;
            var theme = config?.Theme ?? Theme.System;
            var elementTheme = theme switch {
                Theme.Light => ElementTheme.Light,
                Theme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (window.Content is FrameworkElement fe) {
                fe.RequestedTheme = elementTheme;
            }
        }
    }
}
