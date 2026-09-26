namespace Ardel.Launcher.Services.Launch;

internal interface IGameLaunchStage
{
    string Name { get; }
    Task RunAsync(LaunchSession session, CancellationToken cancellationToken);
}
