using System.Diagnostics;

namespace Ardel.Launcher.Services.Launch;

/// <summary>Per-phase stopwatch logging for launch diagnostics.</summary>
internal sealed class LaunchStageTimings
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly List<string> _lines = [];
    private readonly string _logPath;

    public LaunchStageTimings(string versionId)
    {
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "launch_timing.log");
        _lines.Add($"=== Launch {DateTime.Now:O}  version={versionId} ===");
    }

    public void Tick(string phase)
    {
        var line = $"[+{_stopwatch.ElapsedMilliseconds,5} ms] {phase}";
        Debug.WriteLine($"[LaunchTiming] {line}");
        _lines.Add(line);
    }

    public void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllLines(_logPath, _lines);
        }
        catch
        {
            // best-effort
        }
    }
}
