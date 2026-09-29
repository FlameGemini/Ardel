using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Ardel.Launcher.Helpers;

namespace Ardel.Launcher.Services.Update;

public class UpdateService
{
    private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/FlameGemini/Ardel/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Ardel-Launcher");
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        }
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = AboutMetadata.ResolveVersionNumber();

        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiLatestReleaseUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? string.Empty : string.Empty;
        var releaseName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? tagName : tagName;
        var releaseNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? string.Empty : string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? string.Empty : string.Empty;

        string? downloadUrl = null;
        long downloadSize = 0;

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            // First look for Ardel.exe
            foreach (var asset in assetsElem.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? string.Empty : string.Empty;
                if (string.Equals(assetName, "Ardel.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var dUrl) ? dUrl.GetString() : null;
                    downloadSize = asset.TryGetProperty("size", out var sElem) ? sElem.GetInt64() : 0;
                    break;
                }
            }

            // Fallback: look for any .exe asset
            if (string.IsNullOrEmpty(downloadUrl))
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? string.Empty : string.Empty;
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var dUrl) ? dUrl.GetString() : null;
                        downloadSize = asset.TryGetProperty("size", out var sElem) ? sElem.GetInt64() : 0;
                        break;
                    }
                }
            }
        }

        var hasUpdate = !string.IsNullOrWhiteSpace(tagName) && IsNewerVersion(tagName, currentVersion);

        return new UpdateInfo
        {
            HasUpdate = hasUpdate,
            CurrentVersion = currentVersion,
            LatestVersion = tagName,
            ReleaseName = releaseName,
            ReleaseNotes = releaseNotes,
            ReleaseUrl = releaseUrl,
            DownloadUrl = downloadUrl,
            DownloadSize = downloadSize
        };
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("No download URL provided for update.");

        var updateDir = GetUpdatesDirectory();
        Directory.CreateDirectory(updateDir);

        var tempFilePath = Path.Combine(updateDir, $"Ardel_Update_{CleanVersionString(update.LatestVersion)}.exe");

        using var response = await _httpClient.GetAsync(
            update.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.DownloadSize;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                var percentage = (double)totalRead / totalBytes * 100.0;
                progress?.Report(percentage);
            }
        }

        progress?.Report(100.0);
        return tempFilePath;
    }

    public bool ApplyUpdateAndRestart(string downloadedFilePath)
    {
        if (!File.Exists(downloadedFilePath))
            return false;

        var targetExe = ResolveTargetExecutablePath();
        var launcherPid = Environment.ProcessId;
        var bootstrapperPid = 0;

        var bootstrapperPidStr = Environment.GetEnvironmentVariable("ARDEL_BOOTSTRAPPER_PID");
        if (!string.IsNullOrEmpty(bootstrapperPidStr) && int.TryParse(bootstrapperPidStr, out var bPid))
        {
            bootstrapperPid = bPid;
        }

        var updateDir = GetUpdatesDirectory();
        Directory.CreateDirectory(updateDir);
        var scriptPath = Path.Combine(updateDir, "apply_update.cmd");

        var scriptContent =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set LAUNCHER_PID=%1\r\n" +
            "set BOOTSTRAPPER_PID=%2\r\n" +
            "set TARGET_EXE=%~3\r\n" +
            "set TEMP_EXE=%~4\r\n" +
            "\r\n" +
            ":wait_launcher\r\n" +
            "if \"%LAUNCHER_PID%\"==\"0\" goto wait_bootstrapper\r\n" +
            "tasklist /FI \"PID eq %LAUNCHER_PID%\" 2>NUL | find /I \"%LAUNCHER_PID%\" >NUL\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >NUL\r\n" +
            "    goto wait_launcher\r\n" +
            ")\r\n" +
            "\r\n" +
            ":wait_bootstrapper\r\n" +
            "if \"%BOOTSTRAPPER_PID%\"==\"0\" goto do_replace\r\n" +
            "tasklist /FI \"PID eq %BOOTSTRAPPER_PID%\" 2>NUL | find /I \"%BOOTSTRAPPER_PID%\" >NUL\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >NUL\r\n" +
            "    goto wait_bootstrapper\r\n" +
            ")\r\n" +
            "\r\n" +
            ":do_replace\r\n" +
            "timeout /t 1 /nobreak >NUL\r\n" +
            "move /Y \"%TEMP_EXE%\" \"%TARGET_EXE%\" >NUL\r\n" +
            "if errorlevel 1 (\r\n" +
            "    copy /Y \"%TEMP_EXE%\" \"%TARGET_EXE%\" >NUL\r\n" +
            "    del /F /Q \"%TEMP_EXE%\" >NUL\r\n" +
            ")\r\n" +
            "\r\n" +
            "start \"\" \"%TARGET_EXE%\"\r\n" +
            "exit\r\n";

        File.WriteAllText(scriptPath, scriptContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{scriptPath}\" {launcherPid} {bootstrapperPid} \"{targetExe}\" \"{downloadedFilePath}\"\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        };

        Process.Start(psi);
        return true;
    }

    public static string GetUpdatesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "updates");
    }

    public static string ResolveTargetExecutablePath()
    {
        var bootstrapperExe = Environment.GetEnvironmentVariable("ARDEL_BOOTSTRAPPER_EXE");
        if (!string.IsNullOrEmpty(bootstrapperExe) && File.Exists(bootstrapperExe))
            return Path.GetFullPath(bootstrapperExe);

        var portableRoot = Environment.GetEnvironmentVariable("ARDEL_PORTABLE_ROOT");
        if (!string.IsNullOrEmpty(portableRoot))
        {
            var ardelExe = Path.Combine(portableRoot, "Ardel.exe");
            if (File.Exists(ardelExe))
                return Path.GetFullPath(ardelExe);

            var launcherExe = Path.Combine(portableRoot, "Ardel.Launcher.exe");
            if (File.Exists(launcherExe))
                return Path.GetFullPath(launcherExe);
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            return Path.GetFullPath(processPath);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Ardel.exe"));
    }

    public static bool IsNewerVersion(string latestTag, string currentVer)
    {
        var cleanLatest = CleanVersionString(latestTag);
        var cleanCurrent = CleanVersionString(currentVer);

        if (Version.TryParse(cleanLatest, out var vLatest) && Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vLatest > vCurrent;
        }

        return !string.Equals(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase);
    }

    public static string CleanVersionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "0.0.0.0";

        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plusIdx = s.IndexOf('+');
        if (plusIdx >= 0)
            s = s[..plusIdx];

        var dashIdx = s.IndexOf('-');
        if (dashIdx >= 0)
            s = s[..dashIdx];

        return s.Trim();
    }
}
