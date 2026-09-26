using System.Diagnostics;
using System.Text;

namespace Ardel.Launcher;

/// <summary>
/// Lightweight startup phase timings → %LocalAppData%\Ardel\startup.log
/// Watch starts at type initialization (earliest managed entry we control).
/// </summary>
internal static class StartupClock
{
    // Static field init runs before App ctor — approximates process managed start.
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static readonly long ProcessStartMs =
        Environment.TickCount64; // wall clock anchor for header only
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ardel",
        "startup.log");

    private static readonly StringBuilder Buffer = new();

    public static string LogFilePath => LogPath;

    static StartupClock()
    {
        Mark("Process managed start (StartupClock type init)");
    }

    public static void Mark(string phase)
    {
        var ms = Watch.ElapsedMilliseconds;
        var line = $"[+{ms,5} ms] {phase}";
        Debug.WriteLine($"[Startup] {line}");
        lock (Gate)
        {
            Buffer.AppendLine(line);
        }
    }

    public static string GetSnapshot()
    {
        lock (Gate)
        {
            return Buffer.Length == 0
                ? "(no startup marks yet)"
                : Buffer.ToString();
        }
    }

    public static void Flush()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string body;
            lock (Gate)
            {
                body = Buffer.ToString();
            }

            File.WriteAllText(
                LogPath,
                $"Ardel startup {DateTime.Now:O}{Environment.NewLine}" +
                $"Watch elapsed at flush: {Watch.ElapsedMilliseconds} ms " +
                $"(TickCount64={Environment.TickCount64}, startTick={ProcessStartMs}){Environment.NewLine}" +
                body);
        }
        catch
        {
            // never block startup on logging
        }
    }
}
