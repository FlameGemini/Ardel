using System.Diagnostics;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services.SkinRelay;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace Ardel.Launcher.Services.Launch;

internal sealed class ConfigureInstanceStage(GameLaunchHost host) : IGameLaunchStage
{
    public string Name => "configure-instance";

    public Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        session.ReportStatus(Loc.Get(LocKeys.Home_ResolvingJava));

        var instance = host.InstanceSettings.Load(session.VersionId, session.Settings.GameDirectory);
        if (instance.OverrideJava &&
            !string.IsNullOrWhiteSpace(instance.JavaPath) &&
            File.Exists(instance.JavaPath))
        {
            session.Settings.JavaPath = instance.JavaPath;
        }

        var gameDir = string.IsNullOrWhiteSpace(session.Settings.GameDirectory)
            ? GamePaths.GetMinecraftRoot()
            : session.Settings.GameDirectory;

        var memory = MemoryLaunchResolver.Resolve(
            session.Settings,
            instance,
            GamePaths.GetVersionInstanceDirectory(session.VersionId, session.Settings.GameDirectory));

        session.Settings.MaxRamMb = memory.MaxMb;
        session.BindInstance(instance, memory, gameDir);
        session.Timings.Tick("instanceSettings.Load");
        return Task.CompletedTask;
    }
}

internal sealed class ResolveJavaStage(GameLaunchHost host) : IGameLaunchStage
{
    public string Name => "resolve-java";

    public async Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        var metadataUrl = host.ResolveMetadataUrl(session.VersionId, session.Settings.UseBmclApi);
        session.Timings.Tick("metadataUrl resolved");

        var required = await OfficialJavaRequirements
            .ResolveAsync(
                session.VersionId,
                metadataUrl,
                session.Settings.GameDirectory,
                host.GetListingHttpClient(session.Settings),
                cancellationToken)
            .ConfigureAwait(false);
        session.Timings.Tick($"OfficialJavaRequirements.ResolveAsync (required={required})");

        string? javaPath = session.Settings.JavaPath;
        if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
        {
            var actual = JavaLocator.GetJavaVersion(javaPath);
            session.Timings.Tick($"JavaLocator.GetJavaVersion (actual={actual})");
            if (!JavaLocator.IsCompatible(actual, required))
                javaPath = null;
        }
        else
        {
            javaPath = null;
        }

        javaPath ??= JavaRuntimeInstaller.TryFindInstalled(required);
        session.Timings.Tick("TryFindInstalled");
        javaPath ??= JavaLocator.FindBestMatch(required)?.JavaExePath;
        session.Timings.Tick("FindBestMatch");

        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            session.ReportStatus(Loc.Format(LocKeys.Home_DownloadingJava, required));
            javaPath = await JavaRuntimeInstaller
                .EnsureAsync(required, host.GetHttpClient(session.Settings), session.ByteProgress, cancellationToken)
                .ConfigureAwait(false);
            session.Timings.Tick("JavaRuntimeInstaller.EnsureAsync");
        }

        session.JavaPath = JavaRuntimeInstaller.PreferJavaw(javaPath);
        session.Settings.JavaPath = session.JavaPath;
    }
}

internal sealed class AcquireGameFilesStage(GameLaunchHost host) : IGameLaunchStage
{
    public string Name => "acquire-game-files";

    public async Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        var settings = session.Settings;
        var versionId = session.VersionId;
        var gameDir = session.GameDirectory;

        var isReady = host.EvaluateLaunchReady(settings, versionId, gameDir);
        session.Timings.Tick($"IsLaunchReady={isReady}");

        if (isReady)
        {
            session.ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
            session.Launcher = host.CreateLauncherCore(settings, heavyInstaller: false);
            session.Timings.Tick("CreateLauncherCore (fast path)");
            return;
        }

        if (!settings.ResourceRepairEnabled &&
            !settings.ResourceRepairAutoDownload &&
            GamePaths.IsVersionFullyInstalled(versionId, gameDir))
        {
            session.ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
            session.Launcher = host.CreateLauncherCore(settings, heavyInstaller: false);
            session.Timings.Tick("CreateLauncherCore (repair disabled)");
            return;
        }

        try
        {
            session.ReportStatus(Loc.Format(LocKeys.Home_Preparing, versionId));
            session.Launcher = await host
                .EnsureInstalledForLaunchAsync(settings, versionId, session.FileProgress, cancellationToken)
                .ConfigureAwait(false);
            session.ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
            session.Timings.Tick("EnsureInstalledForLaunchAsync");
        }
        catch (Exception ex) when (!settings.ResourceRepairBlockLaunchOnFailure &&
                                   GamePaths.IsVersionFullyInstalled(versionId, gameDir))
        {
            Debug.WriteLine($"[GameLaunchPipeline] Repair failed, continuing: {ex.Message}");
            session.ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
            session.Launcher = host.CreateLauncherCore(settings, heavyInstaller: false);
        }
    }
}

internal sealed class ComposeLaunchProfileStage(GameLaunchHost host) : IGameLaunchStage
{
    public string Name => "compose-launch-profile";

    public async Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        if (session.OnlineSession is null &&
            (string.IsNullOrWhiteSpace(session.PlayerName) ||
             NameRules.ValidatePlayerName(session.PlayerName) is not null))
        {
            throw new InvalidOperationException(Loc.Get(LocKeys.Validate_PlayerEmpty));
        }

        var launcher = session.Launcher ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));
        var instance = session.InstanceSettings;
        var settings = session.Settings;
        var versionId = session.VersionId;
        var trimmedName = session.PlayerName.Trim();

        CmlLib.Core.Auth.MSession sessionAuth;
        if (session.OnlineSession is { } online)
        {
            sessionAuth = new CmlLib.Core.Auth.MSession(
                online.Username,
                online.AccessToken,
                NormalizeUuid(online.Uuid))
            {
                UserType = "msa"
            };
        }
        else
        {
            sessionAuth = MSession.CreateOfflineSession(trimmedName);
            if (session.OfflineSkin is not null && !string.IsNullOrWhiteSpace(session.OfflineSkin.PlayerUuid))
                sessionAuth.UUID = session.OfflineSkin.PlayerUuid.Replace("-", "", StringComparison.Ordinal);
        }

        static string NormalizeUuid(string uuid) =>
            uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

        var root = launcher.MinecraftPath;
        var instanceDir = GamePaths.EnsureVersionIsolation(versionId, settings.GameDirectory);
        var launchPath = new MinecraftPath(instanceDir)
        {
            Library = root.Library,
            Assets = root.Assets,
            Versions = root.Versions,
            Runtime = root.Runtime,
            Resource = root.Resource
        };

        var option = new MLaunchOption
        {
            Session = sessionAuth,
            MaximumRamMb = Math.Clamp(settings.MaxRamMb, 512, 65536),
            JavaPath = session.JavaPath,
            Path = launchPath,
            GameLauncherName = "Ardel",
            GameLauncherVersion = AboutMetadata.ResolveVersionNumber(),
            VersionType = "Ardel"
        };

        if (session.Memory.MinMb > 0)
            option.MinimumRamMb = Math.Clamp(session.Memory.MinMb, 512, option.MaximumRamMb);

        if (instance.ScreenWidth > 0)
            option.ScreenWidth = instance.ScreenWidth;
        if (instance.ScreenHeight > 0)
            option.ScreenHeight = instance.ScreenHeight;
        if (instance.FullScreen)
            option.FullScreen = true;

        if (!string.IsNullOrWhiteSpace(instance.ServerIp))
        {
            option.ServerIp = instance.ServerIp.Trim();
            if (instance.ServerPort is > 0 and <= 65535)
                option.ServerPort = instance.ServerPort;
        }

        var jvmArgs = new List<MArgument>();
        foreach (var arg in ArgumentTokenizer.Split(instance.ExtraJvmArguments))
            jvmArgs.Add(new MArgument(arg));

        var gameArgs = ArgumentTokenizer.Split(instance.ExtraGameArguments);
        if (gameArgs.Count > 0)
            option.ExtraGameArguments = gameArgs.Select(a => new MArgument(a)).ToArray();

        var nativesDir = Path.Combine(root.Versions, versionId, "natives");
        if (Directory.Exists(nativesDir) && Directory.EnumerateFileSystemEntries(nativesDir).Any())
            option.NativesDirectory = nativesDir;

        var offlineSkin = session.OfflineSkin;
        if (offlineSkin is not null && File.Exists(offlineSkin.SkinPngPath))
        {
            session.ReportStatus(Loc.Get(LocKeys.Home_PreparingOfflineSkin));
            session.SkinRelay = await SkinRelaySession.TryStartAsync(
                    host.GetHttpClient(settings),
                    settings.UseBmclApi,
                    offlineSkin.PlayerUuid,
                    trimmedName,
                    offlineSkin.SkinPngPath,
                    offlineSkin.SlimArms,
                    cancellationToken)
                .ConfigureAwait(false);
            if (session.SkinRelay is not null)
                jvmArgs.AddRange(session.SkinRelay.JvmArguments);
        }

        if (jvmArgs.Count > 0)
            option.ExtraJvmArguments = jvmArgs.ToArray();

        session.LaunchOption = option;
        session.Timings.Tick("ComposeLaunchProfile");
    }
}

internal sealed class MaterializeProcessStage(GameLaunchHost host) : IGameLaunchStage
{
    public string Name => "materialize-process";

    public async Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        var launcher = session.Launcher ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));
        var option = session.LaunchOption ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));
        var versionId = session.VersionId;
        var javaPath = session.JavaPath ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));

        Process process;
        // ProcessStartInfo cache is a bare argv snapshot — unsafe when this launch adds
        // --server / extra JVM/game args / window flags (auto-join would silently no-op,
        // and a forced BuildProcessAsync races WarmVersion on version_manifest_v2.json).
        var canReusePsi = session.OnlineSession is null &&
                          session.OfflineSkin is null &&
                          CanReuseCachedProcessInfo(option);
        if (canReusePsi &&
            host.ProcessInfoCache.TryGetValue(versionId, out var cachedPsi))
        {
            var psi = GameLaunchHost.CloneProcessStartInfo(cachedPsi);
            psi.FileName = javaPath;
            process = new Process { StartInfo = psi };
            session.UsedCachedProcessInfo = true;
            session.Timings.Tick("Process (cached ProcessStartInfo)");
        }
        else if (host.VersionCache.TryGetValue(versionId, out var cachedVersion))
        {
            // In-memory BuildProcess applies ServerIp from option — no manifest file I/O.
            process = launcher.BuildProcess(cachedVersion, option);
            session.Timings.Tick("BuildProcess (cached IVersion)");
            if (canReusePsi)
                host.ProcessInfoCache[versionId] = process.StartInfo;
            else
                host.ProcessInfoCache.TryRemove(versionId, out _);
        }
        else
        {
            // BuildProcessAsync/GetVersionAsync also touch version_manifest_v2.json —
            // must not race WarmVersionAsync / InstallAsync.
            process = await host.RunUnderCmlGateAsync(
                    async () =>
                    {
                        var built = await launcher
                            .BuildProcessAsync(versionId, option, cancellationToken)
                            .ConfigureAwait(false);
                        try
                        {
                            var v = await launcher.GetVersionAsync(versionId).ConfigureAwait(false);
                            host.VersionCache[versionId] = v;
                            if (canReusePsi)
                                host.ProcessInfoCache[versionId] = built.StartInfo;
                            else
                                host.ProcessInfoCache.TryRemove(versionId, out _);
                        }
                        catch
                        {
                            // best-effort cache warm
                        }

                        return built;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            session.Timings.Tick("BuildProcessAsync (no cache)");
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        if (!string.IsNullOrWhiteSpace(process.StartInfo.FileName))
            process.StartInfo.FileName = JavaRuntimeInstaller.PreferJavaw(process.StartInfo.FileName);

        session.Process = process;
    }

    /// <summary>
    /// Cached ProcessStartInfo omits per-launch option differences. Only reuse when argv is plain.
    /// </summary>
    private static bool CanReuseCachedProcessInfo(MLaunchOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.ServerIp))
            return false;
        if (option.ServerPort is > 0)
            return false;
        if (option.ScreenWidth is > 0 || option.ScreenHeight is > 0)
            return false;
        if (option.FullScreen == true)
            return false;
        if (option.ExtraJvmArguments is not null && option.ExtraJvmArguments.Any())
            return false;
        if (option.ExtraGameArguments is not null && option.ExtraGameArguments.Any())
            return false;
        if (option.MinimumRamMb is > 0)
            return false;
        return true;
    }
}

internal sealed class StartGameProcessStage : IGameLaunchStage
{
    public string Name => "start-process";

    public Task RunAsync(LaunchSession session, CancellationToken cancellationToken)
    {
        var process = session.Process ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));

        cancellationToken.ThrowIfCancellationRequested();
        session.ReportStatus(Loc.Get(LocKeys.Home_LaunchingGame));

        if (!process.Start())
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));

        session.Timings.Tick("process.Start — TOTAL");
        session.Timings.Flush();

        if (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx)
            {
                Debug.WriteLine($"[GameLaunchPipeline] Cancel kill failed: {killEx.Message}");
            }

            session.SkinRelay?.Dispose();
            session.SkinRelay = null;
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (session.SkinRelay is not null)
        {
            session.SkinRelay.AttachToProcess(process);
            session.SkinRelay = null;
        }

        return Task.CompletedTask;
    }
}
