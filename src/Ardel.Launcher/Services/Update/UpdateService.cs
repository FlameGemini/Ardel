using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Ardel.Launcher.Helpers;

namespace Ardel.Launcher.Services.Update;

public class UpdateService
{
    private const string GitHubApiReleasesUrl = "https://api.github.com/repos/FlameGemini/Ardel/releases?per_page=10";
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

        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiReleasesUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        JsonElement latestRelease = default;
        var foundRelease = false;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var rel in root.EnumerateArray())
            {
                var isDraft = rel.TryGetProperty("draft", out var dElem) && dElem.GetBoolean();
                if (!isDraft)
                {
                    latestRelease = rel;
                    foundRelease = true;
                    break;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            latestRelease = root;
            foundRelease = true;
        }

        if (!foundRelease)
        {
            return new UpdateInfo
            {
                HasUpdate = false,
                CurrentVersion = currentVersion
            };
        }

        var tagName = latestRelease.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? string.Empty : string.Empty;
        var releaseName = latestRelease.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? tagName : tagName;
        var releaseNotes = latestRelease.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? string.Empty : string.Empty;
        var releaseUrl = latestRelease.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? string.Empty : string.Empty;

        string? downloadUrl = null;
        long downloadSize = 0;

        if (latestRelease.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
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
            LatestVersion = CleanVersionString(tagName),
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

        // Clean up previous temporary update artifacts
        try
        {
            foreach (var file in Directory.GetFiles(updateDir))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }

        var tempFilePath = Path.Combine(updateDir, $"Ardel_Update_{CleanVersionString(update.LatestVersion)}.exe");

        var candidates = BuildDownloadCandidates(update.DownloadUrl);
        Exception? lastEx = null;

        foreach (var url in candidates)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    continue;

                var totalBytes = response.Content.Headers.ContentLength ?? update.DownloadSize;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    tempFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 524288,
                    useAsync: true);

                var buffer = new byte[524288]; // 512 KB buffer for high-throughput downloads
                long totalRead = 0;
                int bytesRead;
                double lastReportedPct = -1;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage = (double)totalRead / totalBytes * 100.0;
                        if (percentage - lastReportedPct >= 0.5 || percentage >= 99.9)
                        {
                            lastReportedPct = percentage;
                            progress?.Report(percentage);
                        }
                    }
                }

                progress?.Report(100.0);
                return tempFilePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Download candidate '{url}' failed: {ex.Message}");
                lastEx = ex;
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }

        throw new HttpRequestException($"Failed to download update from all candidates. Last error: {lastEx?.Message}", lastEx);
    }

    private static List<string> BuildDownloadCandidates(string rawUrl)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(rawUrl))
            return list;

        if (rawUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            list.Add($"https://mirror.ghproxy.com/{rawUrl}");
            list.Add($"https://ghproxy.net/{rawUrl}");
            list.Add($"https://gh-proxy.com/{rawUrl}");
        }

        list.Add(rawUrl);
        return list;
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
            "chcp 65001 >nul\r\n" +
            "\r\n" +
            "set MAX_WAIT=30\r\n" +
            "set COUNT=0\r\n" +
            "\r\n" +
            ":wait_loop\r\n" +
            "set /a COUNT+=1\r\n" +
            "if %COUNT% gtr %MAX_WAIT% goto force_kill\r\n" +
            "\r\n" +
            $"tasklist /fi \"PID eq {launcherPid}\" 2>nul | find \"{launcherPid}\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    powershell -NoProfile -Command \"Start-Sleep -Milliseconds 300\" >nul 2>nul\r\n" +
            "    goto wait_loop\r\n" +
            ")\r\n" +
            "\r\n" +
            "goto do_replace\r\n" +
            "\r\n" +
            ":force_kill\r\n" +
            $"taskkill /PID {launcherPid} /F >nul 2>nul\r\n" +
            (bootstrapperPid > 0 ? $"taskkill /PID {bootstrapperPid} /F >nul 2>nul\r\n" : "") +
            "powershell -NoProfile -Command \"Start-Sleep -Milliseconds 500\" >nul 2>nul\r\n" +
            "\r\n" +
            ":do_replace\r\n" +
            "set RETRY=0\r\n" +
            ":copy_loop\r\n" +
            "set /a RETRY+=1\r\n" +
            $"copy /Y \"{downloadedFilePath}\" \"{targetExe}\" >nul 2>nul\r\n" +
            "if errorlevel 1 (\r\n" +
            "    if %RETRY% leq 15 (\r\n" +
            "        powershell -NoProfile -Command \"Start-Sleep -Milliseconds 500\" >nul 2>nul\r\n" +
            "        goto copy_loop\r\n" +
            "    )\r\n" +
            ")\r\n" +
            "\r\n" +
            $"del /F /Q \"{downloadedFilePath}\" >nul 2>nul\r\n" +
            $"start \"\" \"{targetExe}\"\r\n" +
            "del /F /Q \"%~f0\" >nul 2>nul & exit\r\n";

        File.WriteAllText(scriptPath, scriptContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true
        };

        Process.Start(psi);
        Environment.Exit(0);
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
