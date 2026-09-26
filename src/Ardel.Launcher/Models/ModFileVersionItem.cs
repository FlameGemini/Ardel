namespace Ardel.Launcher.Models;

public enum ModReleaseChannel
{
    Release,
    Beta,
    Alpha
}

/// <summary>One publishable file/version of a Mod catalog project.</summary>
public sealed class ModFileVersionItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }
    public required ModReleaseChannel Channel { get; init; }
    public required IReadOnlyList<string> GameVersions { get; init; }
    public required IReadOnlyList<string> Loaders { get; init; }
    public DateTimeOffset? Published { get; init; }
    public string PublishedLabel { get; init; } = string.Empty;
    public bool HasPublished => !string.IsNullOrEmpty(PublishedLabel);
    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = Array.Empty<ModDependencyRef>();

    public string ChannelCode => Channel switch
    {
        ModReleaseChannel.Beta => "B",
        ModReleaseChannel.Alpha => "A",
        _ => "R"
    };

    /// <summary>Optional Modrinth/CurseForge content hash (lowercase hex).</summary>
    public string? Sha1 { get; init; }

    /// <summary>Preformatted at create time — avoid re-sorting on every bind.</summary>
    public string GameVersionsLabel { get; init; } = string.Empty;

    public string LoadersLabel { get; init; } = string.Empty;

    public bool HasGameVersions => !string.IsNullOrEmpty(GameVersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);

    /// <summary>Identified mods inside a modpack version (rich rows for the contents page).</summary>
    public IReadOnlyList<ModpackContentItem> IncludedContents { get; init; } = Array.Empty<ModpackContentItem>();

    /// <summary>Display names only (compat / tooltip preview).</summary>
    public IReadOnlyList<string> IncludedMods { get; init; } = Array.Empty<string>();

    public string IncludedModsLabel { get; init; } = string.Empty;

    /// <summary>Full mod list for tooltip — keep the row itself compact.</summary>
    public string IncludedModsTooltip { get; init; } = string.Empty;

    public bool HasIncludedMods => IncludedContents.Count > 0 || !string.IsNullOrEmpty(IncludedModsLabel);

    public string IncludedModsCountLabel { get; init; } = string.Empty;
}
