namespace Ardel.Launcher.Models;

/// <summary>Subset of world fields shown on the save properties page.</summary>
public sealed class SaveWorldMetadata
{
    public bool Found { get; init; }
    public string? LevelName { get; init; }
    public int? GameType { get; init; }
    public int? Difficulty { get; init; }
    public long? Seed { get; init; }
    public bool? AllowCommands { get; init; }
    public int? SpawnX { get; init; }
    public int? SpawnY { get; init; }
    public int? SpawnZ { get; init; }
    public string? GameVersion { get; init; }
    public int? DataVersion { get; init; }
    public long? LastPlayed { get; init; }
    public long? Time { get; init; }
    public bool? Hardcore { get; init; }
}
