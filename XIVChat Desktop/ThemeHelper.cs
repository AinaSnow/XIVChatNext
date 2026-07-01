using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace XIVChat_Desktop {
    public static class ThemeHelper {
        private static readonly HashSet<Window> ActiveWindows = new HashSet<Window>();

        public static void InitializeWindow(Window window) {
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
