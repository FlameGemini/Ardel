using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ardel.Launcher.Helpers;

/// <summary>Sets a process main-window title after Minecraft creates its HWND.</summary>
internal static class GameWindowTitle
{
    public static void TryApply(Process process, string? titleTemplate, string versionId, string playerName)
    {
        if (process is null || string.IsNullOrWhiteSpace(titleTemplate))
            return;

        var title = Expand(titleTemplate, versionId, playerName);
        if (string.IsNullOrWhiteSpace(title))
            return;

        try
        {
            process.Refresh();
            var hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
                return;

            _ = SetWindowText(hwnd, title);
        }
        catch
        {
            // best-effort — game may recreate the window
        }
    }

    public static string Expand(string template, string versionId, string playerName)
    {
        var text = template.Trim();
        return text
            .Replace("{version}", versionId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", versionId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", playerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{player}", playerName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);
}
