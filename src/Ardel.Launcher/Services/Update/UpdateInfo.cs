namespace Ardel.Launcher.Services.Update;

public class UpdateInfo
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
    public long DownloadSize { get; init; }
}
