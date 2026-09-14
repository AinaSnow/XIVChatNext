namespace XIVChat_Desktop;
internal static class SetupText {
    internal static string T(string chinese, string english) => LocalizationHelper.LanguageCode.StartsWith("zh") ? chinese : english;
}
