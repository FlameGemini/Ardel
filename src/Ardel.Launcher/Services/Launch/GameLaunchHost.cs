using System.Collections.Concurrent;
using System.Diagnostics;
using Ardel.Launcher.Models;
using CmlLib.Core;
using CmlLib.Core.Version;

namespace Ardel.Launcher.Services.Launch;

/// <summary>Internal surface used by launch pipeline stages.</summary>
internal sealed class GameLaunchHost
{
    private readonly MinecraftLaunchService _service;

    public GameLaunchHost(MinecraftLaunchService service) => _service = service;

    public InstanceSettingsStore InstanceSettings => _service.InstanceSettingsAccessor;

    public ConcurrentDictionary<string, IVersion> VersionCache => _service.VersionCacheAccessor;

    public ConcurrentDictionary<string, ProcessStartInfo> ProcessInfoCache =>
        _service.ProcessInfoCacheAccessor;

    public string? ResolveMetadataUrl(string versionId, bool useBmclApi) =>
        _service.ResolveMetadataUrlForLaunch(versionId, useBmclApi);

    public HttpClient GetHttpClient(LauncherSettings settings) =>
        _service.GetHttpClientForLaunch(settings);

    public HttpClient GetListingHttpClient(LauncherSettings settings) =>
        _service.GetListingHttpClientForLaunch(settings);

    public MinecraftLauncher CreateLauncherCore(LauncherSettings settings, bool heavyInstaller) =>
        _service.CreateLauncherCore(settings, heavyInstaller);

    public MinecraftLauncher CreateLauncher(LauncherSettings settings) =>
        _service.CreateLauncher(settings);

    public Task<MinecraftLauncher> EnsureInstalledForLaunchAsync(
        LauncherSettings settings,
        string versionId,
        IProgress<FileProgressInfo>? fileProgress,
        CancellationToken cancellationToken) =>
        _service.EnsureInstalledForLaunchInternalAsync(settings, versionId, fileProgress, cancellationToken);

    public bool EvaluateLaunchReady(LauncherSettings settings, string versionId, string gameDir) =>
        _service.EvaluateLaunchReadyInternal(settings, versionId, gameDir);

    public Task<T> RunUnderCmlGateAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken) =>
        _service.RunUnderCmlGateAsync(work, cancellationToken);

    public static ProcessStartInfo CloneProcessStartInfo(ProcessStartInfo source) =>
        MinecraftLaunchService.CloneProcessStartInfoPublic(source);
}
