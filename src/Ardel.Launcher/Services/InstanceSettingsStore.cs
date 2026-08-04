using System.Diagnostics;
using System.Text.Json;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Loads / saves per-instance settings beside the version folder markers.
/// File: <c>{versions}/{id}/ardel-instance.json</c>.
/// </summary>
public sealed class InstanceSettingsStore
{
    public const string FileName = "ardel-instance.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public InstanceSettings Load(string versionId, string? minecraftRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        var path = GetPath(versionId, minecraftRoot);
        try
        {
            if (!File.Exists(path))
                return new InstanceSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstanceSettings>(json, JsonOptions)
                   ?? new InstanceSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettings] Load failed for {versionId}: {ex}");
            return new InstanceSettings();
        }
    }

    public void Save(string versionId, InstanceSettings settings, string? minecraftRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentNullException.ThrowIfNull(settings);

        settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
        settings.MaxRamMb = Math.Clamp(settings.MaxRamMb, 512, 65536);
        if (settings.MinRamMb > 0)
            settings.MinRamMb = Math.Clamp(settings.MinRamMb, 512, settings.MaxRamMb);
        else
            settings.MinRamMb = 0;
        settings.Notes = settings.Notes?.Trim() ?? string.Empty;
        settings.JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath)
            ? null
            : settings.JavaPath.Trim();
        settings.ExtraJvmArguments = settings.ExtraJvmArguments?.Trim() ?? string.Empty;
        settings.ExtraGameArguments = settings.ExtraGameArguments?.Trim() ?? string.Empty;
        settings.ServerIp = settings.ServerIp?.Trim() ?? string.Empty;
        if (settings.ServerPort < 0)
            settings.ServerPort = 0;
        if (settings.ScreenWidth < 0)
            settings.ScreenWidth = 0;
        if (settings.ScreenHeight < 0)
            settings.ScreenHeight = 0;

        var dir = GamePaths.GetVersionInstanceDirectory(versionId, minecraftRoot);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GetPath(string versionId, string? minecraftRoot = null)
    {
        var dir = GamePaths.GetVersionInstanceDirectory(versionId, minecraftRoot);
        return Path.Combine(dir, FileName);
    }
}
