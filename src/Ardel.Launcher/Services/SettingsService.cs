using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Loads / saves preferences to <c>{launcherRoot}/config/launcher_config.json</c>
/// (portable, next to the exe / repo). Migrates once from the legacy
/// <c>%LocalAppData%\Ardel\settings.json</c> if present.
/// Game files always live in <c>{launcherRoot}/.minecraft</c>.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly object _gate = new();
    private LauncherSettings? _cache;

    public SettingsService()
    {
        try
        {
            var dir = Path.Combine(GamePaths.GetLauncherDirectory(), "config");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "launcher_config.json");
        }
        catch
        {
            var fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ardel", "config");
            try { Directory.CreateDirectory(fallbackDir); } catch { /* ignore */ }
            _settingsPath = Path.Combine(fallbackDir, "launcher_config.json");
        }
    }

    public string SettingsPath => _settingsPath;

    /// <summary>Clears the in-memory cache so the next <see cref="Load"/> reads from disk.</summary>
    public void InvalidateCache()
    {
        lock (_gate)
            _cache = null;
    }

    public LauncherSettings Load()
    {
        lock (_gate)
        {
            if (_cache is not null)
                return Clone(_cache);

            try
            {
                LauncherSettings settings;
                if (File.Exists(_settingsPath))
                {
                    settings = ReadFile(_settingsPath) ?? CreateDefault();
                }
                else if (TryMigrateLegacy(out var migrated))
                {
                    settings = migrated;
                    TryWrite(settings);
                }
                else
                {
                    settings = CreateDefault();
                    TryWrite(settings);
                }

                var dirty = false;

                if (settings.SchemaVersion < 3)
                {
                    // Do not keep the old "force Chinese when Windows is zh" behavior —
                    // empty UiLanguage means follow system, and the user can pick English.
                    settings.SchemaVersion = 3;
                    dirty = true;
                }

                if (settings.SchemaVersion < 4)
                {
                    // Portable config path introduced.
                    settings.SchemaVersion = 4;
                    dirty = true;
                }

                if (settings.SchemaVersion < 5)
                {
                    // Ardo / behavior / resource repair / crash analysis defaults.
                    NormalizePreferenceDefaults(settings);
                    settings.SchemaVersion = 5;
                    dirty = true;
                }
                else
                {
                    NormalizePreferenceDefaults(settings);
                }

                if (settings.SchemaVersion < 6)
                {
                    // Home quick-launch button preference (default on for existing installs).
                    settings.ShowHomeQuickLaunch = true;
                    settings.SchemaVersion = 6;
                    dirty = true;
                }

                if (settings.SchemaVersion < 7)
                {
                    settings.ShowHomeWeather = true;
                    settings.ShowHomeCalendar = true;
                    settings.SchemaVersion = 7;
                    dirty = true;
                }

                if (settings.SchemaVersion < 8)
                {
                    // Grandfather existing installs; fresh defaults keep HasCompletedOobe=false.
                    if (settings.SchemaVersion > 0)
                        settings.HasCompletedOobe = true;
                    settings.SchemaVersion = 8;
                    dirty = true;
                }

                if (settings.SchemaVersion < 9)
                {
                    // About legal v3: require re-accept via OOBE for prior installs.
                    // Grandfather only when already on v3+ acceptance (fresh after this ship).
                    if (settings.AcceptedAboutLegalVersion <= 0 && settings.HasCompletedOobe)
                        settings.AcceptedAboutLegalVersion = 0;
                    settings.SchemaVersion = 9;
                    dirty = true;
                }

                // No invented default name — clear invalid / legacy CJK placeholders.
                if (!string.IsNullOrEmpty(settings.PlayerName) &&
                    Helpers.NameRules.ValidatePlayerName(settings.PlayerName) is not null)
                {
                    settings.PlayerName = string.Empty;
                    dirty = true;
                }

                // Always force portable .minecraft next to the launcher.
                var portable = GamePaths.GetMinecraftRoot();
                if (!PathsEqual(settings.GameDirectory, portable))
                {
                    settings.GameDirectory = portable;
                    dirty = true;
                }

                settings.ForceVersionIsolation = true;
                settings.InstanceOrder ??= [];
                settings.UiLanguage ??= string.Empty;
                settings.AppTheme = string.IsNullOrWhiteSpace(settings.AppTheme)
                    ? "Default"
                    : settings.AppTheme.Trim();

                if (dirty)
                    TryWrite(settings);

                _cache = Clone(settings);
                return Clone(_cache);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Load failed: {ex}");
                var fallback = CreateDefault();
                _cache = Clone(fallback);
                return fallback;
            }
        }
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            try
            {
                settings.GameDirectory = GamePaths.GetMinecraftRoot();
                settings.ForceVersionIsolation = true;
                settings.InstanceOrder ??= [];
                settings.UiLanguage ??= string.Empty;
                settings.AppTheme = string.IsNullOrWhiteSpace(settings.AppTheme)
                    ? "Default"
                    : settings.AppTheme.Trim();
                if (settings.SchemaVersion < 5)
                    settings.SchemaVersion = 5;
                if (settings.SchemaVersion < 6)
                {
                    settings.ShowHomeQuickLaunch = true;
                    settings.SchemaVersion = 6;
                }
                if (settings.SchemaVersion < 7)
                {
                    settings.ShowHomeWeather = true;
                    settings.ShowHomeCalendar = true;
                    settings.SchemaVersion = 7;
                }
                NormalizePreferenceDefaults(settings);

                WriteFileAtomic(_settingsPath, settings);
                _cache = Clone(settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Save failed: {ex}");
                throw;
            }
        }
    }

    public static string GetDefaultGameDirectory() => GamePaths.GetMinecraftRoot();

    private static string LegacyAppDataPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "settings.json");

    private bool TryMigrateLegacy(out LauncherSettings settings)
    {
        settings = CreateDefault();
        try
        {
            var legacy = LegacyAppDataPath;
            if (!File.Exists(legacy))
                return false;

            var migrated = ReadFile(legacy);
            if (migrated is null)
                return false;

            settings = migrated;
            if (settings.SchemaVersion < 5)
                settings.SchemaVersion = 5;
            settings.HasCompletedOobe = true;
            settings.SchemaVersion = Math.Max(settings.SchemaVersion, 8);
            NormalizePreferenceDefaults(settings);

            Debug.WriteLine($"[SettingsService] Migrated legacy settings from {legacy}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Legacy migrate failed: {ex.Message}");
            return false;
        }
    }

    private static LauncherSettings? ReadFile(string path)
    {
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        return JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions);
    }

    private void TryWrite(LauncherSettings settings)
    {
        try
        {
            WriteFileAtomic(_settingsPath, settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Write failed: {ex}");
        }
    }

    private static void WriteFileAtomic(string path, LauncherSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var jsonOut = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tmp, jsonOut);
        File.Move(tmp, path, overwrite: true);
    }

    private static LauncherSettings Clone(LauncherSettings s) => new()
    {
        SchemaVersion = s.SchemaVersion,
        HasCompletedOobe = s.HasCompletedOobe,
        GameDirectory = s.GameDirectory,
        SelectedVersion = s.SelectedVersion,
        PlayerName = s.PlayerName,
        JavaPath = s.JavaPath,
        MaxRamMb = s.MaxRamMb,
        MemoryMode = s.MemoryMode,
        UseBmclApi = s.UseBmclApi,
        ForceVersionIsolation = s.ForceVersionIsolation,
        UiLanguage = s.UiLanguage ?? string.Empty,
        AppTheme = string.IsNullOrWhiteSpace(s.AppTheme) ? "Default" : s.AppTheme,
        InstanceOrder = s.InstanceOrder is null ? [] : [.. s.InstanceOrder],
        QuickLaunchVersionId = string.IsNullOrWhiteSpace(s.QuickLaunchVersionId)
            ? null
            : s.QuickLaunchVersionId.Trim(),
        ShowHomeQuickLaunch = s.ShowHomeQuickLaunch,
        ShowHomeWeather = s.ShowHomeWeather,
        ShowHomeCalendar = s.ShowHomeCalendar,
        WeatherLocationDisplay = s.WeatherLocationDisplay ?? string.Empty,
        WeatherLocationQuery = s.WeatherLocationQuery ?? string.Empty,
        WeatherLatitude = s.WeatherLatitude,
        WeatherLongitude = s.WeatherLongitude,
        WeatherTimezone = s.WeatherTimezone ?? string.Empty,
        WeatherUseFahrenheit = s.WeatherUseFahrenheit,
        HomeClockUse12Hour = s.HomeClockUse12Hour,
        ArdoDownloadThreads = s.ArdoDownloadThreads,
        ArdoSmallDownloadThreads = s.ArdoSmallDownloadThreads,
        ArdoSpeedLimitKbps = s.ArdoSpeedLimitKbps,
        ArdoChunkSizeMb = s.ArdoChunkSizeMb,
        ArdoVerifySha1 = s.ArdoVerifySha1,
        ArdoMaxRetries = s.ArdoMaxRetries,
        PostLaunchWindowAction = s.PostLaunchWindowAction,
        BmclUsageMode = s.BmclUsageMode,
        OpenGameLogViewer = s.OpenGameLogViewer,
        ResourceRepairEnabled = s.ResourceRepairEnabled,
        ResourceRepairConcurrency = s.ResourceRepairConcurrency,
        ResourceVerifyMode = s.ResourceVerifyMode,
        ResourceRepairAutoDownload = s.ResourceRepairAutoDownload,
        ResourceRepairWriteReadyMarker = s.ResourceRepairWriteReadyMarker,
        ResourceRepairBlockLaunchOnFailure = s.ResourceRepairBlockLaunchOnFailure,
        CrashAnalysisEnabled = s.CrashAnalysisEnabled,
        CrashAnalysisOnForceKill = s.CrashAnalysisOnForceKill,
        CrashAnalysisAutoOpenLogs = s.CrashAnalysisAutoOpenLogs,
        CrashAnalysisKeepRecent = s.CrashAnalysisKeepRecent,
        CrashAnalysisPreIndexLogs = s.CrashAnalysisPreIndexLogs,
        CrashAnalysisVerboseDebug = s.CrashAnalysisVerboseDebug,
        CrashAnalysisShowConfidence = s.CrashAnalysisShowConfidence,
        ShowStartupSplash = s.ShowStartupSplash,
        StartupSplashShowProgressBar = s.StartupSplashShowProgressBar,
        StartupSplashDurationMs = s.StartupSplashDurationMs,
        StartupSplashShowBrandName = s.StartupSplashShowBrandName,
        AcceptedAboutLegalVersion = s.AcceptedAboutLegalVersion
    };

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static LauncherSettings CreateDefault()
    {
        var settings = new LauncherSettings
        {
            SchemaVersion = 9,
            HasCompletedOobe = false,
            GameDirectory = GamePaths.GetMinecraftRoot(),
            MaxRamMb = Math.Clamp(GetSuggestedRamMb(), 1024, 16384),
            UseBmclApi = false,
            UiLanguage = "en-US",
            AppTheme = "Default",
            PlayerName = string.Empty,
            ForceVersionIsolation = true
        };
        NormalizePreferenceDefaults(settings);
        return settings;
    }

    private static void NormalizePreferenceDefaults(LauncherSettings settings)
    {
        settings.ArdoDownloadThreads = Math.Clamp(
            settings.ArdoDownloadThreads <= 0
                ? ArdoInstallOptions.DefaultDownloadThreads
                : settings.ArdoDownloadThreads,
            1,
            256);
        settings.ArdoSmallDownloadThreads = Math.Clamp(
            settings.ArdoSmallDownloadThreads <= 0
                ? ArdoInstallOptions.DefaultSmallDownloadThreads
                : settings.ArdoSmallDownloadThreads,
            settings.ArdoDownloadThreads,
            256);
        settings.ArdoSpeedLimitKbps = Math.Max(0, settings.ArdoSpeedLimitKbps);
        settings.ArdoChunkSizeMb = Math.Clamp(
            settings.ArdoChunkSizeMb <= 0
                ? ArdoInstallOptions.DefaultChunkSizeMb
                : settings.ArdoChunkSizeMb,
            1,
            64);
        settings.ArdoMaxRetries = Math.Clamp(
            settings.ArdoMaxRetries <= 0
                ? ArdoInstallOptions.DefaultMaxRetries
                : settings.ArdoMaxRetries,
            1,
            32);
        settings.ResourceRepairConcurrency = Math.Clamp(
            settings.ResourceRepairConcurrency <= 0
                ? ArdoInstallOptions.DefaultCheckConcurrency
                : settings.ResourceRepairConcurrency,
            1,
            256);
        settings.CrashAnalysisKeepRecent = Math.Clamp(
            settings.CrashAnalysisKeepRecent <= 0 ? 10 : settings.CrashAnalysisKeepRecent,
            1,
            50);

        if (settings.BmclUsageMode is not (
                (int)BmclUsageMode.Always or (int)BmclUsageMode.FallbackWhenOfficialSlow))
            settings.BmclUsageMode = (int)BmclUsageMode.FallbackWhenOfficialSlow;

        if (settings.PostLaunchWindowAction is < 0 or > (int)PostLaunchWindowAction.ExitLauncher)
            settings.PostLaunchWindowAction = (int)PostLaunchWindowAction.None;

        if (settings.ResourceVerifyMode is < 0 or > (int)ResourceVerifyMode.FullSha1)
            settings.ResourceVerifyMode = (int)ResourceVerifyMode.TrustReadyMarker;

        settings.StartupSplashDurationMs = settings.StartupSplashDurationMs <= 0
            ? StartupSplashOptions.DefaultDurationMs
            : Math.Clamp(
                settings.StartupSplashDurationMs,
                StartupSplashOptions.MinDurationMs,
                StartupSplashOptions.MaxDurationMs);
    }

    private static int GetSuggestedRamMb()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var halfMb = (int)(bytes / 1024 / 1024 / 2);
            return Math.Clamp(halfMb, 2048, 8192);
        }
        catch
        {
            return 4096;
        }
    }
}
