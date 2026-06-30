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
                    UpdateTitleBar(window, elementTheme);
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
            UpdateTitleBar(window, elementTheme);
        }

        private static void UpdateTitleBar(Window window, ElementTheme theme) {
            try {
                var titleBar = window.AppWindow?.TitleBar;
                if (titleBar != null) {
                    bool isDark = theme == ElementTheme.Dark ||
                                  (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

                    titleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    titleBar.ButtonHoverBackgroundColor = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
                    titleBar.ButtonPressedBackgroundColor = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0);
                }
            } catch { }
        }
    }
}
