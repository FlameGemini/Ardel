namespace Ardel.Launcher.Models;

public sealed class InstanceSessionRecord
{
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int DurationSeconds { get; set; }
    public int? ExitCode { get; set; }
}

public sealed class InstanceStatsData
{
    public long TotalPlaySeconds { get; set; }
    public int LaunchCount { get; set; }
    public DateTime? LastSessionStartUtc { get; set; }
    public DateTime? LastSessionEndUtc { get; set; }
    public List<InstanceSessionRecord> Sessions { get; set; } = [];
}

public sealed class PlayDayBarItem
{
    public required string Label { get; init; }
    public int Seconds { get; init; }
    public double BarHeight { get; init; }
    public required string DurationText { get; init; }
    public required string ToolTip { get; init; }
    public double BarOpacity => Seconds > 0 ? 0.9 : 0.28;
}

public sealed class ScreenshotThumbItem
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Image { get; init; }
}
