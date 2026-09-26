namespace Ardel.Launcher.Models;

public enum PostLaunchWindowAction
{
    None = 0,
    Minimize = 1,
    MinimizeUntilExit = 2,
    ExitLauncher = 3
}

public enum BmclUsageMode
{
    FallbackWhenOfficialSlow = 0,
    Always = 1
}

public enum ResourceVerifyMode
{
    TrustReadyMarker = 0,
    QuickExistence = 1,
    FullSha1 = 2
}
