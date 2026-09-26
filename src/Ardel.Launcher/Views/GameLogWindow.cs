using System.Diagnostics;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Views;

/// <summary>
/// Spawns <c>Ardel.LogViewer.exe</c> as a true independent process/window.
/// WinUI secondary windows share app lifetime and often exit Ardel when the game closes.
/// </summary>
public sealed class GameLogWindow
{
    private Process? _process;

    public GameLogWindow(string instanceDirectory)
    {
        var logPath = Path.Combine(instanceDirectory, "logs", "latest.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            if (!File.Exists(logPath))
                File.WriteAllText(logPath, string.Empty);
        }
        catch
        {
            // Viewer can still wait for the file.
        }

        var exe = Path.Combine(AppContext.BaseDirectory, "Ardel.LogViewer.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException(Loc.Get(LocKeys.GameLog_Title) + ": Ardel.LogViewer.exe missing", exe);

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = Quote(logPath),
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = AppContext.BaseDirectory
        });
    }

    public void ActivateIndependent()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                // Best-effort focus — viewer is its own process.
                _process.Refresh();
            }
        }
        catch
        {
            // ignore
        }
    }

    public void StopTailing()
    {
        // Leave the viewer process running so the user can still read the log.
    }

    public void Close()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.CloseMainWindow();
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        try { _process?.Dispose(); } catch { /* ignore */ }
        _process = null;
    }

    public bool IsDisposed => _process is null;

    private static string Quote(string path)
    {
        if (path.Contains('"', StringComparison.Ordinal))
            path = path.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{path}\"";
    }
}
