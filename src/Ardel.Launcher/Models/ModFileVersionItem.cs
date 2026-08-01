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
    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = Array.Empty<ModDependencyRef>();

    public string ChannelCode => Channel switch
    {
        ModReleaseChannel.Beta => "B",
        ModReleaseChannel.Alpha => "A",
        _ => "R"
    };

    /// <summary>Preformatted at create time — avoid re-sorting on every bind.</summary>
    public string GameVersionsLabel { get; init; } = string.Empty;

    public string LoadersLabel { get; init; } = string.Empty;

    public bool HasGameVersions => !string.IsNullOrEmpty(GameVersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);
}
