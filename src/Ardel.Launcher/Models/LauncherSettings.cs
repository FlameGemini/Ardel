namespace Ardel.Launcher.Models;

/// <summary>
/// Persisted launcher preferences.
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>Bump when applying one-time preference migrations.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// UI culture override. Empty = follow Windows display language.
    /// Supported: <c>en-US</c>, <c>zh-CN</c>, <c>ja-JP</c>. Empty = follow Windows.
    /// </summary>
    public string UiLanguage { get; set; } = string.Empty;

    public string PlayerName { get; set; } = Localization.Loc.Get(Localization.LocKeys.Default_PlayerName);
    public string? SelectedVersion { get; set; }
    public string? JavaPath { get; set; }
    public int MaxRamMb { get; set; } = 4096;
    public bool UseBmclApi { get; set; } = false;

    /// <summary>Always <c>{exe}/.minecraft</c> …portable next to the launcher.</summary>
    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>Forced on: each version gets its own mods/saves/config under versions/id.</summary>
    public bool ForceVersionIsolation { get; set; } = true;
}

