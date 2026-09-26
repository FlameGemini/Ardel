using System.Diagnostics;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services.SkinRelay;

namespace Ardel.Launcher.Services.Launch;

/// <summary>Ordered launch pipeline: instance → Java → assets → profile → process → start.</summary>
internal sealed class GameLaunchPipeline
{
    private readonly IReadOnlyList<IGameLaunchStage> _stages;

    public GameLaunchPipeline(MinecraftLaunchService service)
    {
        var host = new GameLaunchHost(service);
        _stages =
        [
            new ConfigureInstanceStage(host),
            new ResolveJavaStage(host),
            new AcquireGameFilesStage(host),
            new ComposeLaunchProfileStage(host),
            new MaterializeProcessStage(host),
            new StartGameProcessStage()
        ];
    }

    public async Task<Process> ExecuteAsync(
        LauncherSettings settings,
        string versionId,
        string playerName,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken,
        OfflineSkinLaunchOptions? offlineSkin = null,
        OnlineLaunchSession? onlineSession = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        var session = new LaunchSession
        {
            Settings = settings,
            VersionId = versionId,
            PlayerName = playerName,
            OfflineSkin = onlineSession is null ? offlineSkin : null,
            OnlineSession = onlineSession,
            FileProgress = fileProgress,
            ByteProgress = byteProgress,
            Timings = new LaunchStageTimings(versionId)
        };

        try
        {
            foreach (var stage in _stages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stage.RunAsync(session, cancellationToken).ConfigureAwait(false);
                session.Timings.Tick($"stage:{stage.Name}");
            }

            return session.Process ?? throw new InvalidOperationException("Launch pipeline completed without a process.");
        }
        catch (Exception ex)
        {
            session.SkinRelay?.Dispose();
            Debug.WriteLine($"[GameLaunchPipeline] Launch failed at pipeline: {ex}");
            throw;
        }
    }
}
