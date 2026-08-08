namespace Ardel.Launcher.Models;

/// <summary>
/// Per-instance launch overrides, stored as <c>ardel-instance.json</c>
/// next to the version's isolated mods/saves/config.
/// </summary>
public sealed class InstanceSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Optional note shown only in the launcher.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>When true, use <see cref="JavaPath"/> instead of the launcher default.</summary>
    public bool OverrideJava { get; set; }

    public string? JavaPath { get; set; }

    /// <summary>When true, use <see cref="MaxRamMb"/> / <see cref="MinRamMb"/> instead of launcher defaults.</summary>
    public bool OverrideMemory { get; set; }

    public int MaxRamMb { get; set; } = 4096;

    /// <summary>0 = leave JVM default minimum.</summary>
    public int MinRamMb { get; set; }

    /// <summary>Extra JVM args (whitespace / newlines; quotes supported).</summary>
    public string ExtraJvmArguments { get; set; } = string.Empty;

    /// <summary>Extra game args (whitespace / newlines; quotes supported).</summary>
    public string ExtraGameArguments { get; set; } = string.Empty;

    /// <summary>0 = leave default.</summary>
    public int ScreenWidth { get; set; }

    /// <summary>0 = leave default.</summary>
    public int ScreenHeight { get; set; }

    public bool FullScreen { get; set; }

    /// <summary>Optional auto-join server host. Empty = none.</summary>
    public string ServerIp { get; set; } = string.Empty;

    /// <summary>Used when <see cref="ServerIp"/> is set. 0 = Minecraft default (25565).</summary>
    public int ServerPort { get; set; }

    /// <summary>Segoe Fluent/MDL2 icon glyph character representation (e.g. "\uE7FC").</summary>
    public string? IconGlyph { get; set; }
}
