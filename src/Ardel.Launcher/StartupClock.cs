using System.Diagnostics;
using System.Text;

namespace Ardel.Launcher;

/// <summary>
/// Lightweight startup phase timings → %LocalAppData%\Ardel\startup.log
/// </summary>
internal static class StartupClock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ardel",
        "startup.log");

    private static readonly StringBuilder Buffer = new();

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

            File.WriteAllText(LogPath, $"Ardel startup {DateTime.Now:O}{Environment.NewLine}{body}");
        }
        catch
        {
            // never block startup on logging
        }
    }
}
