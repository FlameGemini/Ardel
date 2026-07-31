namespace Ardel.Launcher.Models;

/// <summary>Full Mod project page payload (summary + file versions).</summary>
public sealed class ModProjectDetail
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SourceLabel { get; init; }
    public required string ProjectUrl { get; init; }
    public string? IconUrl { get; init; }
    public Uri? IconUri { get; init; }
    public bool HasIcon => IconUri is not null;
    public string VersionsLabel { get; init; } = string.Empty;
    public string LoadersLabel { get; init; } = string.Empty;
    public bool HasVersions => !string.IsNullOrEmpty(VersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);
    public required IReadOnlyList<ModFileVersionItem> Files { get; init; }
}
