namespace Ardel.Launcher.Localization;

/// <summary>British English spelling overrides. Unlisted keys fall back to en-US.</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, string> EnglishUk = new(StringComparer.Ordinal)
    {
        [LocKeys.InstanceSettings_NoOnlineMatch] = "No online catalogue match.",
        [LocKeys.Settings_ThemeDescDefault] = "Follows Windows light/dark mode with the same palette as Light and Dark, and Mica.",
        [LocKeys.Settings_ThemeDescDark] = "A sleek dark interface with a soft grey accent.",
        [LocKeys.Settings_ThemeDescSamoyed] = "A cozy white fluffy Samoyed fur texture background with cool blue-grey accents. — Dedicated to the memory of my beloved Samoyed.",
    };
}
