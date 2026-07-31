namespace Ardel.Launcher.Models;

/// <summary>One Mod catalog hit shown in the download Mod list.</summary>
public sealed class ModProjectItem
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SourceLabel { get; init; }
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }
    public string DownloadsLabel { get; init; } = string.Empty;
}
