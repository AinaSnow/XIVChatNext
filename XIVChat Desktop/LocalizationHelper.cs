using System;
using Windows.Globalization;

namespace XIVChat_Desktop {
    public enum AppLanguage {
        System,
        ChineseSimplified,
        English
    }

    public static class LocalizationHelper {
        public static void Initialize(AppLanguage language) {
            ApplyLanguage(language);
        }

        private static string _currentLangCode = string.Empty;

        public static void ApplyLanguage(AppLanguage language) {
            _currentLangCode = language switch {
                AppLanguage.ChineseSimplified => "zh-CN",
                AppLanguage.English => "en-US",
                _ => System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US"
            };
            _resourceManager = null;
        }

        public static AppLanguage[] AvailableLanguages => new[] {
            AppLanguage.System,
            AppLanguage.ChineseSimplified,
            AppLanguage.English
        };

        public static string GetLanguageName(AppLanguage language) => language switch {
            AppLanguage.System => "跟随系统 / System Default",
            AppLanguage.ChineseSimplified => "简体中文 (Chinese)",
            AppLanguage.English => "English",
            _ => language.ToString()
        };

        private static Microsoft.Windows.ApplicationModel.Resources.ResourceManager? _resourceManager;

        public static string GetString(string key) {
            string path = key.Replace('.', '/');
            try {
                if (_resourceManager == null) {
                    string priPath = System.IO.Path.Combine(AppContext.BaseDirectory, "XIVChat Desktop.pri");
                    if (System.IO.File.Exists(priPath)) {
                        _resourceManager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager(priPath);
                    } else {
                        _resourceManager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
                    }
                }

                var map = _resourceManager.MainResourceMap;
                var context = _resourceManager.CreateResourceContext();
                string langCode = _currentLangCode;
                if (string.IsNullOrEmpty(langCode)) {
                    langCode = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
                }
                context.QualifierValues["Language"] = langCode;

                var subTree = map.GetSubtree("Resources");
                if (subTree != null) {
                    var candidate = subTree.TryGetValue(key, context) ?? subTree.TryGetValue(path, context);
                    if (candidate != null) {
                        return candidate.ValueAsString;
                    }
                }

                var candidateMain = map.TryGetValue("Resources/" + path, context)
                                 ?? map.TryGetValue(key, context)
                                 ?? map.TryGetValue(path, context);
                if (candidateMain != null) {
                    return candidateMain.ValueAsString;
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"[FAIL] Key='{key}' Path='{path}' Lang='{langCode}' MapCount={map.ResourceCount} SubtreeNull={(subTree == null)}");
                for (uint i = 0; i < Math.Min(map.ResourceCount, 15); i++) {
                    var pair = map.GetValueByIndex(i);
                    sb.AppendLine($"  {i}: {pair.Key} = {pair.Value?.ValueAsString}");
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "loc_debug.log"), sb.ToString() + "\r\n");

                return key;
            } catch (Exception ex) {
                try {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "loc_debug.log"), $"[EX] Key='{key}': {ex}\r\n");
                } catch { }
                return key;
            }
        }
    }
}
