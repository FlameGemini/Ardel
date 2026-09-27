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
            var settings = JsonSerializer.Deserialize<InstanceSettings>(json, JsonOptions)
                   ?? new InstanceSettings();
            MigrateMemoryMode(settings);
            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettings] Load failed for {versionId}: {ex}");
            return new InstanceSettings();
        }
    }

    private static void MigrateMemoryMode(InstanceSettings settings)
    {
        // Older files only had OverrideMemory; keep both fields in sync.
        if (settings.MemoryMode is < 0 or > 2)
            settings.MemoryMode = (int)InstanceMemoryMode.FollowDefault;

        if (settings.MemoryMode == (int)InstanceMemoryMode.FollowDefault && settings.OverrideMemory)
            settings.MemoryMode = (int)InstanceMemoryMode.Custom;
        else if (settings.MemoryMode == (int)InstanceMemoryMode.Custom)
            settings.OverrideMemory = true;
        else if (settings.MemoryMode is (int)InstanceMemoryMode.FollowDefault or (int)InstanceMemoryMode.Dynamic)
            settings.OverrideMemory = false;
    }

    /// <summary>
    /// List-scan helper: read notes/glyph without creating version folders.
    /// </summary>
    public static (string Notes, string IconGlyph) TryReadListFields(
        string versionDirectory)
    {
        var path = Path.Combine(versionDirectory, FileName);
        try
        {
            if (!File.Exists(path))
                return (string.Empty, "\uE7FC");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var notes = root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? string.Empty
                : string.Empty;
            var glyph = root.TryGetProperty("iconGlyph", out var g) && g.ValueKind == JsonValueKind.String
                ? g.GetString()
                : null;
            return (notes, string.IsNullOrWhiteSpace(glyph) ? "\uE7FC" : glyph!);
        }
        catch
        {
            return (string.Empty, "\uE7FC");
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

        if (settings.MemoryMode is < 0 or > 2)
            settings.MemoryMode = settings.OverrideMemory
                ? (int)InstanceMemoryMode.Custom
                : (int)InstanceMemoryMode.FollowDefault;
        settings.OverrideMemory = settings.MemoryMode == (int)InstanceMemoryMode.Custom;

        settings.Notes = settings.Notes?.Trim() ?? string.Empty;
        settings.JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath)
            ? null
            : settings.JavaPath.Trim();
        settings.ExtraJvmArguments = settings.ExtraJvmArguments?.Trim() ?? string.Empty;
        settings.ExtraGameArguments = settings.ExtraGameArguments?.Trim() ?? string.Empty;
        settings.WindowTitle = settings.WindowTitle?.Trim() ?? string.Empty;
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
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static string GetPath(string versionId, string? minecraftRoot = null)
    {
        var dir = GamePaths.GetVersionInstanceDirectory(versionId, minecraftRoot);
        return Path.Combine(dir, FileName);
    }
}
