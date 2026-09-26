namespace Ardel.Launcher.Models;

/// <summary>Runtime options for <see cref="Services.ArdelGameInstaller"/>.</summary>
public sealed class ArdoInstallOptions
{
    public const int DefaultDownloadThreads = 64;
    public const int DefaultSmallDownloadThreads = 64;
    public const int DefaultCheckConcurrency = 64;
    public const int DefaultMaxRetries = 3;
    public const int DefaultChunkSizeMb = 4;

    public int DownloadThreads { get; init; } = DefaultDownloadThreads;
    public int SmallDownloadThreads { get; init; } = DefaultSmallDownloadThreads;
    public int CheckConcurrency { get; init; } = DefaultCheckConcurrency;
    public int MaxRetries { get; init; } = DefaultMaxRetries;
    public int SpeedLimitKbps { get; init; }
    public int ChunkSizeBytes { get; init; } = DefaultChunkSizeMb * 1024 * 1024;
    public bool VerifySha1 { get; init; } = true;

    public static ArdoInstallOptions FromSettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var threads = Math.Clamp(settings.ArdoDownloadThreads, 1, 256);
        var small = Math.Clamp(settings.ArdoSmallDownloadThreads, threads, 256);
        var check = Math.Clamp(settings.ResourceRepairConcurrency, 1, 256);
        var retries = Math.Clamp(settings.ArdoMaxRetries, 0, 32);
        var chunkMb = Math.Clamp(settings.ArdoChunkSizeMb, 1, 64);
        return new ArdoInstallOptions
        {
            DownloadThreads = threads,
            SmallDownloadThreads = small,
            CheckConcurrency = check,
            MaxRetries = Math.Max(1, retries),
            SpeedLimitKbps = Math.Max(0, settings.ArdoSpeedLimitKbps),
            ChunkSizeBytes = chunkMb * 1024 * 1024,
            VerifySha1 = settings.ArdoVerifySha1
        };
    }
}
