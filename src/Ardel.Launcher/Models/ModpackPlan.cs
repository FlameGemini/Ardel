namespace Ardel.Launcher.Models;

/// <summary>Parsed modpack ready to install into a new instance.</summary>
public sealed class ModpackPlan
{
    public required string Name { get; init; }
    public required string MinecraftVersion { get; init; }
    public required ModLoaderKind Loader { get; init; }
    public string? LoaderVersion { get; init; }
    public required IReadOnlyList<ModpackRemoteFile> RemoteFiles { get; init; }
    public required string ArchivePath { get; init; }
    public required ModpackArchiveKind ArchiveKind { get; init; }
}

public enum ModpackArchiveKind
{
    Mrpack,
    CurseForgeZip
}

public sealed class ModpackRemoteFile
{
    /// <summary>Path relative to the instance root (e.g. mods/foo.jar).</summary>
    public required string RelativePath { get; init; }

    public required IReadOnlyList<string> DownloadUrls { get; init; }
    public long? FileSize { get; init; }
    public string? Sha1 { get; init; }
}
