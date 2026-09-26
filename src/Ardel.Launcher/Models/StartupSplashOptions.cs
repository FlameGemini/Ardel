namespace Ardel.Launcher.Models;

/// <summary>Runtime options for the in-window startup splash overlay.</summary>
public sealed class StartupSplashOptions
{
    public const int DefaultDurationMs = 2000;
    public const int MinDurationMs = 1000;
    public const int MaxDurationMs = 12000;
    public const int DurationStepMs = 250;

    public bool Enabled { get; init; } = true;
    public bool ShowProgressBar { get; init; } = true;
    public bool ShowBrandName { get; init; } = true;
    public int DurationMs { get; init; } = DefaultDurationMs;

    public static StartupSplashOptions FromSettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new StartupSplashOptions
        {
            Enabled = settings.ShowStartupSplash,
            ShowProgressBar = settings.StartupSplashShowProgressBar,
            ShowBrandName = settings.StartupSplashShowBrandName,
            DurationMs = Math.Clamp(settings.StartupSplashDurationMs, MinDurationMs, MaxDurationMs)
        };
    }
}
