using System.Diagnostics;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Progress without CmlLib types — keeps cold start free of CmlLib.dll.
/// </summary>
public readonly record struct FileProgressInfo(string Name, int ProgressedTasks, int TotalTasks);

public readonly record struct ByteProgressInfo(long ProgressedBytes, long TotalBytes);

/// <summary>
/// Minecraft install / launch facade (implementation may load CmlLib lazily).
/// </summary>
public interface IMinecraftLaunchService
{
    Task<IReadOnlyList<GameVersionItem>> GetVersionsAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Drop in-memory version manifest cache (e.g. after switching download mirror).</summary>
    void InvalidateVersionCache();

    /// <summary>
    /// Drop cached ProcessStartInfo for a version (or all) after instance launch options change
    /// (auto-join server, JVM args, etc.).
    /// </summary>
    void InvalidateLaunchProcessCache(string? versionId = null);

    Task InstallAsync(
        LauncherSettings settings,
        string versionId,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default);

    /// <summary>Install vanilla and optionally Fabric / Forge / NeoForge under a custom version name.</summary>
    Task<string> InstallAsync(
        LauncherSettings settings,
        InstallRequest request,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default);

    /// <summary>List Fabric / Forge / NeoForge / OptiFine builds available for a Minecraft version.</summary>
    Task<IReadOnlyList<ModLoaderVersionOption>> GetModLoaderVersionsAsync(
        LauncherSettings settings,
        string minecraftVersionId,
        ModLoaderKind loader,
        CancellationToken cancellationToken = default);

    /// <summary>List OptiFine builds for a Minecraft version (optionally filtered for Forge compatibility).</summary>
    Task<IReadOnlyList<ModLoaderVersionOption>> GetOptiFineVersionsAsync(
        LauncherSettings settings,
        string minecraftVersionId,
        string? forgeVersionFilter = null,
        CancellationToken cancellationToken = default);

    Task<Process> LaunchAsync(
        LauncherSettings settings,
        string versionId,
        string playerName,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default,
        OfflineSkinLaunchOptions? offlineSkin = null,
        OnlineLaunchSession? onlineSession = null);

    /// <summary>
    /// Best-effort background pre-warm: loads and caches the CmlLib version object
    /// so the next <see cref="LaunchAsync"/> call can skip the ~1500 ms JSON parse.
    /// Safe to call concurrently or redundantly — no-op if already cached.
    /// </summary>
    Task WarmVersionAsync(
        LauncherSettings settings,
        string versionId,
        CancellationToken cancellationToken = default);
}

public static class MinecraftLaunchServiceFactory
{
    public static IMinecraftLaunchService Create(
        SettingsService settingsService,
        InstanceSettingsStore? instanceSettings = null) =>
        new MinecraftLaunchService(settingsService, instanceSettings);
}
