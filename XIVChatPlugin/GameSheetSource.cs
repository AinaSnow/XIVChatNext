using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina;
using Lumina.Excel;

namespace XIVChatPlugin {
    // The offline constructor exercises exactly the same readers against installed game files.
    internal sealed class GameSheetSource {
        private readonly IDataManager? manager;
        private readonly GameData? files;
        private readonly ClientLanguage language;
        internal GameSheetSource(IDataManager manager) => this.manager = manager;
        internal GameSheetSource(GameData files, ClientLanguage language) { this.files = files; this.language = language; }
        internal GameData GameData => this.manager?.GameData ?? this.files!;
        internal ClientLanguage Language => this.manager?.Language ?? this.language;
        internal GameSheetSource Freeze() => new(this.GameData, this.Language);
        private Lumina.Data.Language ExcelLanguage => System.Enum.TryParse<Lumina.Data.Language>(this.Language.ToString(), out var result)
            ? result : this.GameData.Options.DefaultExcelLanguage;
        internal ExcelSheet<T> GetExcelSheet<T>() where T : struct, IExcelRow<T> =>
            this.manager != null ? this.manager.GetExcelSheet<T>() : this.GameData.Excel.GetSheet<T>(this.ExcelLanguage);
        internal SubrowExcelSheet<T> GetSubrowExcelSheet<T>() where T : struct, IExcelSubrow<T> =>
            this.manager != null ? this.manager.GetSubrowExcelSheet<T>() : this.GameData.Excel.GetSubrowSheet<T>(this.ExcelLanguage);
    }
}
