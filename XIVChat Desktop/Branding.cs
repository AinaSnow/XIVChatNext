using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace XIVChat_Desktop {
    internal static class Branding {
        public static void ApplyWindowIcon(Window window) {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "logo-c1.ico");
            if (File.Exists(iconPath)) {
                window.AppWindow.SetIcon(iconPath);
            }
        }
    }
}
