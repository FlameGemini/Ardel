using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Loads / saves preferences under %LocalAppData%\Ardel.
/// Game files always live in <c>{exe}/.minecraft</c> (portable next to the launcher).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly object _gate = new();

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel");

        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public LauncherSettings Load()
    {
        lock (_gate)
        {
            try
            {
                LauncherSettings settings;
                if (!File.Exists(_settingsPath))
                {
                    settings = CreateDefault();
                }
                else
                {
                    var json = File.ReadAllText(_settingsPath);
                    settings = JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions)
                               ?? CreateDefault();
                }

                var dirty = false;

                if (settings.SchemaVersion < 3)
                {
                    // Do not keep the old "force Chinese when Windows is zh" behavior —
                    // empty UiLanguage means follow system, and the user can pick English.
                    settings.SchemaVersion = 3;
                    dirty = true;
                }

                // Always force portable .minecraft next to the exe (ignore legacy AppData paths).
                var portable = GamePaths.GetMinecraftRoot();
                if (!PathsEqual(settings.GameDirectory, portable))
                {
                    settings.GameDirectory = portable;
                    dirty = true;
                }

                settings.ForceVersionIsolation = true;
                if (dirty)
                    TryWrite(settings);

                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Load failed: {ex}");
                return CreateDefault();
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
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] Save failed: {ex}");
                throw;
            }
        }
    }

    public static string GetDefaultGameDirectory() => GamePaths.GetMinecraftRoot();

    private void TryWrite(LauncherSettings settings)
    {
        try
        {
            var jsonOut = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, jsonOut);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Write failed: {ex}");
        }
    }

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

    private static LauncherSettings CreateDefault() => new()
    {
        SchemaVersion = 3,
        GameDirectory = GamePaths.GetMinecraftRoot(),
        MaxRamMb = Math.Clamp(GetSuggestedRamMb(), 1024, 16384),
        UseBmclApi = false,
        UiLanguage = string.Empty,
        PlayerName = Localization.Loc.Get(Localization.LocKeys.Default_PlayerName),
        ForceVersionIsolation = true
    };

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

