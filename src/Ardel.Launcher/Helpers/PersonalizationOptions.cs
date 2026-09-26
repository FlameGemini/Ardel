using System.Collections.ObjectModel;
using Ardel.Launcher.Localization;
using Ardel.Launcher.ViewModels;
using Microsoft.UI;

namespace Ardel.Launcher.Helpers;

/// <summary>Shared language/theme option lists for Settings and OOBE.</summary>
public static class PersonalizationOptions
{
    public static void FillLanguageOptions(ObservableCollection<LanguageOption> target, string? selectedCode)
    {
        target.Clear();
        target.Add(CreateLanguageOption(string.Empty, Loc.Get(LocKeys.Settings_LanguageSystem), selectedCode));
        target.Add(CreateLanguageOption("en-US", Loc.Get(LocKeys.Settings_LanguageEnglishUS), selectedCode));
        target.Add(CreateLanguageOption("en-UK", Loc.Get(LocKeys.Settings_LanguageEnglishUK), selectedCode));
        target.Add(CreateLanguageOption("zh-CN", Loc.Get(LocKeys.Settings_LanguageChinese), selectedCode));
        target.Add(CreateLanguageOption("zh-Hant", Loc.Get(LocKeys.Settings_LanguageChineseTraditional), selectedCode));
        target.Add(CreateLanguageOption("ja-JP", Loc.Get(LocKeys.Settings_LanguageJapanese), selectedCode));
        target.Add(CreateLanguageOption("fr", Loc.Get(LocKeys.Settings_LanguageFrench), selectedCode));
        target.Add(CreateLanguageOption("es", Loc.Get(LocKeys.Settings_LanguageSpanish), selectedCode));
        target.Add(CreateLanguageOption("ko-KR", Loc.Get(LocKeys.Settings_LanguageKorean), selectedCode));
        target.Add(CreateLanguageOption("de", Loc.Get(LocKeys.Settings_LanguageGerman), selectedCode));
        target.Add(CreateLanguageOption("pt-BR", Loc.Get(LocKeys.Settings_LanguagePortuguese), selectedCode));
        target.Add(CreateLanguageOption("it", Loc.Get(LocKeys.Settings_LanguageItalian), selectedCode));
        target.Add(CreateLanguageOption("ru", Loc.Get(LocKeys.Settings_LanguageRussian), selectedCode));
    }

    public static void FillThemeOptions(ObservableCollection<ThemeOption> target, string? selectedCode)
    {
        target.Clear();
        // Preview colors mirror AppThemeController palettes (canvas / rail / chrome / accent).
        target.Add(CreateThemeOption(
            "Default",
            Loc.Get(LocKeys.Settings_ThemeDefault),
            Loc.Get(LocKeys.Settings_ThemeDescDefault),
            accent: ColorHelper.FromArgb(255, 0, 120, 212),
            canvas: ColorHelper.FromArgb(255, 245, 247, 248),
            rail: ColorHelper.FromArgb(255, 236, 239, 241),
            chrome: ColorHelper.FromArgb(255, 210, 216, 222),
            selectedCode));
        target.Add(CreateThemeOption(
            "Light",
            Loc.Get(LocKeys.Settings_ThemeLight),
            Loc.Get(LocKeys.Settings_ThemeDescLight),
            accent: ColorHelper.FromArgb(255, 0, 120, 212),
            canvas: ColorHelper.FromArgb(255, 245, 247, 248),
            rail: ColorHelper.FromArgb(255, 236, 239, 241),
            chrome: ColorHelper.FromArgb(255, 236, 239, 241),
            selectedCode));
        target.Add(CreateThemeOption(
            "Dark",
            Loc.Get(LocKeys.Settings_ThemeDark),
            Loc.Get(LocKeys.Settings_ThemeDescDark),
            accent: ColorHelper.FromArgb(255, 122, 137, 150),
            canvas: ColorHelper.FromArgb(255, 18, 21, 24),
            rail: ColorHelper.FromArgb(255, 26, 29, 32),
            chrome: ColorHelper.FromArgb(255, 26, 29, 32),
            selectedCode));
        target.Add(CreateThemeOption(
            "Sakura",
            Loc.Get(LocKeys.Settings_ThemeSakura),
            Loc.Get(LocKeys.Settings_ThemeDescSakura),
            accent: ColorHelper.FromArgb(255, 232, 64, 150),
            canvas: ColorHelper.FromArgb(255, 255, 228, 240),
            rail: ColorHelper.FromArgb(255, 255, 182, 214),
            chrome: ColorHelper.FromArgb(255, 255, 182, 214),
            selectedCode));
        target.Add(CreateThemeOption(
            "Samoyed",
            Loc.Get(LocKeys.Settings_ThemeSamoyed),
            Loc.Get(LocKeys.Settings_ThemeDescSamoyed),
            accent: ColorHelper.FromArgb(255, 74, 105, 132),
            canvas: ColorHelper.FromArgb(255, 245, 247, 250),
            rail: ColorHelper.FromArgb(255, 194, 208, 222),
            chrome: ColorHelper.FromArgb(255, 237, 241, 245),
            selectedCode));
        target.Add(CreateThemeOption(
            "Sweden",
            Loc.Get(LocKeys.Settings_ThemeSweden),
            Loc.Get(LocKeys.Settings_ThemeDescSweden),
            accent: ColorHelper.FromArgb(255, 0, 106, 167),
            canvas: ColorHelper.FromArgb(255, 245, 248, 252),
            rail: ColorHelper.FromArgb(255, 214, 230, 242),
            chrome: ColorHelper.FromArgb(255, 255, 220, 80),
            selectedCode));
    }

    public static void RefreshLanguageSelection(ObservableCollection<LanguageOption> options, string? selectedCode)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var selected = string.Equals(option.Code, selectedCode ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected == selected)
                continue;
            options[i] = option with { IsSelected = selected };
        }
    }

    public static void RefreshThemeSelection(ObservableCollection<ThemeOption> options, string? selectedCode)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var selected = string.Equals(option.Code, selectedCode ?? "Default", StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected == selected)
                continue;
            options[i] = option with { IsSelected = selected };
        }
    }

    private static LanguageOption CreateLanguageOption(string code, string label, string? selectedCode) =>
        new(code, label, string.Equals(code, selectedCode ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    private static ThemeOption CreateThemeOption(
        string code,
        string label,
        string description,
        Windows.UI.Color accent,
        Windows.UI.Color canvas,
        Windows.UI.Color rail,
        Windows.UI.Color chrome,
        string? selectedCode) =>
        new(code, label, description, accent, canvas, rail, chrome,
            string.Equals(code, selectedCode ?? "Default", StringComparison.OrdinalIgnoreCase));
}
