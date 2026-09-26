using System.Diagnostics;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services.SkinRelay;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;

namespace Ardel.Launcher.Services.Launch;

/// <summary>Mutable state carried through the game launch pipeline.</summary>
internal sealed class LaunchSession
{
    public required LauncherSettings Settings { get; init; }
    public required string VersionId { get; init; }
    public required string PlayerName { get; init; }
    public OfflineSkinLaunchOptions? OfflineSkin { get; init; }
    public OnlineLaunchSession? OnlineSession { get; init; }
    public IProgress<FileProgressInfo>? FileProgress { get; init; }
    public IProgress<ByteProgressInfo>? ByteProgress { get; init; }

    public InstanceSettings InstanceSettings { get; private set; } = new();
    public MemoryLaunchResolver.Result Memory { get; private set; }
    public string? JavaPath { get; set; }
    public string GameDirectory { get; private set; } = string.Empty;
    public MinecraftLauncher? Launcher { get; set; }
    public MLaunchOption? LaunchOption { get; set; }
    public SkinRelaySession? SkinRelay { get; set; }
    public Process? Process { get; set; }
    public bool UsedCachedProcessInfo { get; set; }

    public LaunchStageTimings Timings { get; init; } = null!;

    public void BindInstance(InstanceSettings instance, MemoryLaunchResolver.Result memory, string gameDirectory)
    {
        InstanceSettings = instance;
        Memory = memory;
        GameDirectory = gameDirectory;
    }

    public void ReportStatus(string status) =>
        FileProgress?.Report(new FileProgressInfo(status, 0, 0));
}
