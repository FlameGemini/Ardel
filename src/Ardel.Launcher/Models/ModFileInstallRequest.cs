namespace Ardel.Launcher.Models;

/// <summary>Queue a single Mod jar download into an instance mods folder.</summary>
public sealed class ModFileInstallRequest
{
    /// <summary>Task list title, e.g. "Sodium → 1.21.1-fabric".</summary>
    public required string DisplayName { get; init; }

    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }
    public required string TargetInstanceId { get; init; }
    public required string ModsDirectory { get; init; }
}
