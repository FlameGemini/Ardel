namespace Ardel.Launcher.Models;

/// <summary>Queue a full modpack install (new isolated instance).</summary>
public sealed class ModpackInstallRequest
{
    /// <summary>Task list title.</summary>
    public required string DisplayName { get; init; }

    public required string PackDownloadUrl { get; init; }
    public required string SourceId { get; init; }
    public required string InstanceName { get; init; }
    public string? PackTitle { get; init; }
}
