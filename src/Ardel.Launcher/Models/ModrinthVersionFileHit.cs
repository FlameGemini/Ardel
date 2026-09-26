namespace Ardel.Launcher.Models;

/// <summary>A Modrinth <c>version_file</c> hit usable as an mrpack <c>files[]</c> entry.</summary>
public sealed class ModrinthVersionFileHit
{
    public required string DownloadUrl { get; init; }
    public required string Sha1 { get; init; }
    public string? Sha512 { get; init; }
    public long? FileSize { get; init; }
    public string? FileName { get; init; }
}
