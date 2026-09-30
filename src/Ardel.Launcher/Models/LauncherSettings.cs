namespace Ardel.Launcher.Models;

/// <summary>
/// Persisted launcher preferences.
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>Bump when applying one-time preference migrations.</summary>
    public int SchemaVersion { get; set; } = 8;

    /// <summary>When false, first-run OOBE is shown on launch.</summary>
    public bool HasCompletedOobe { get; set; }

    /// <summary>
    /// UI culture override. Empty = follow Windows display language.
    /// Supported: <c>en-US</c>, <c>en-UK</c>, <c>zh-CN</c>, <c>zh-Hant</c>, <c>ja-JP</c>, <c>fr</c>. Empty = follow Windows.
    /// </summary>
    public string UiLanguage { get; set; } = string.Empty;

    public string PlayerName { get; set; } = string.Empty;
    public string? SelectedVersion { get; set; }
    public string? JavaPath { get; set; }
    public int MaxRamMb { get; set; } = 4096;

    /// <summary>
    /// Default memory policy: <see cref="DefaultMemoryMode.Custom"/> or
    /// <see cref="DefaultMemoryMode.Dynamic"/>.
    /// </summary>
    public int MemoryMode { get; set; } = (int)DefaultMemoryMode.Custom;

    public bool UseBmclApi { get; set; } = false;

    /// <summary>Always <c>{exe}/.minecraft</c> …portable next to the launcher.</summary>
    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>Forced on: each version gets its own mods/saves/config under versions/id.</summary>
    public bool ForceVersionIsolation { get; set; } = true;

    /// <summary>App UI theme: Default, Light, or Dark.</summary>
    public string AppTheme { get; set; } = "Default";

    /// <summary>User-defined instance list order (version ids). Missing ids append by install time.</summary>
    public List<string> InstanceOrder { get; set; } = [];

    /// <summary>
    /// Single instance id pinned for Home quick-launch. Null/empty = none.
    /// </summary>
    public string? QuickLaunchVersionId { get; set; }

    /// <summary>Show the quick-launch button on the Home page.</summary>
    public bool ShowHomeQuickLaunch { get; set; } = true;

    /// <summary>Show weather widget on Home (requires configured region).</summary>
    public bool ShowHomeWeather { get; set; } = true;

    /// <summary>Show date / weekday / international observance on Home.</summary>
    public bool ShowHomeCalendar { get; set; } = true;

    /// <summary>Display name for the configured weather place.</summary>
    public string WeatherLocationDisplay { get; set; } = string.Empty;

    /// <summary>Last search query typed in Home widgets settings.</summary>
    public string WeatherLocationQuery { get; set; } = string.Empty;

    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }

    /// <summary>IANA timezone for the configured place (e.g. Asia/Shanghai).</summary>
    public string WeatherTimezone { get; set; } = string.Empty;

    /// <summary>When true, show °F; otherwise °C.</summary>
    public bool WeatherUseFahrenheit { get; set; }

    /// <summary>When true, Home clock uses 12-hour time with English AM/PM; otherwise 24-hour.</summary>
    public bool HomeClockUse12Hour { get; set; }

    // Ardo download engine
    public int ArdoDownloadThreads { get; set; } = ArdoInstallOptions.DefaultDownloadThreads;
    public int ArdoSmallDownloadThreads { get; set; } = ArdoInstallOptions.DefaultSmallDownloadThreads;
    public int ArdoSpeedLimitKbps { get; set; }
    public int ArdoChunkSizeMb { get; set; } = ArdoInstallOptions.DefaultChunkSizeMb;
    public bool ArdoVerifySha1 { get; set; } = true;
    public int ArdoMaxRetries { get; set; } = ArdoInstallOptions.DefaultMaxRetries;

    // Launcher behavior
    public int PostLaunchWindowAction { get; set; } // PostLaunchWindowAction.None
    public int BmclUsageMode { get; set; } // BmclUsageMode.FallbackWhenOfficialSlow
    public bool OpenGameLogViewer { get; set; }

    // Resource repair
    public bool ResourceRepairEnabled { get; set; } = true;
    public int ResourceRepairConcurrency { get; set; } = ArdoInstallOptions.DefaultCheckConcurrency;
    public int ResourceVerifyMode { get; set; } // ResourceVerifyMode.TrustReadyMarker
    public bool ResourceRepairAutoDownload { get; set; } = true;
    public bool ResourceRepairWriteReadyMarker { get; set; } = true;
    public bool ResourceRepairBlockLaunchOnFailure { get; set; }

    // Crash analysis
    public bool CrashAnalysisEnabled { get; set; } = true;
    public bool CrashAnalysisOnForceKill { get; set; }
    public bool CrashAnalysisAutoOpenLogs { get; set; }
    public int CrashAnalysisKeepRecent { get; set; } = 10;
    public bool CrashAnalysisPreIndexLogs { get; set; }
    public bool CrashAnalysisVerboseDebug { get; set; }
    public bool CrashAnalysisShowConfidence { get; set; }

    // Startup splash / launch badge
    public bool ShowStartupSplash { get; set; } = false;
    public bool StartupSplashShowProgressBar { get; set; } = true;
    public int StartupSplashDurationMs { get; set; } = StartupSplashOptions.DefaultDurationMs;
    public bool StartupSplashShowBrandName { get; set; } = true;

    /// <summary>Last accepted <see cref="Localization.AboutLegalNotice.Version"/>; 0 = never / must re-accept.</summary>
    public int AcceptedAboutLegalVersion { get; set; }
}

