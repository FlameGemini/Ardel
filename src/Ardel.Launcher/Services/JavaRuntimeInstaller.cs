using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Services;

/// <summary>
/// Downloads a portable Temurin JRE/JDK into <c>{programDir}/java/{major}</c> when none is usable.
/// </summary>
internal static class JavaRuntimeInstaller
{
    private const string AdoptiumLatest =
        "https://api.adoptium.net/v3/binary/latest/{0}/ga/windows/x64/{1}/hotspot/normal/eclipse";

    /// <summary>Root for auto-downloaded JREs: next to the launcher exe.</summary>
    public static string GetJavaRoot() =>
        Path.Combine(GamePaths.GetLauncherDirectory(), "java");

    public static string GetInstallDir(int major) =>
        Path.Combine(GetJavaRoot(), major.ToString());

    /// <summary>Returns javaw.exe (preferred) or java.exe under a previous Ardel Java install.</summary>
    public static string? TryFindInstalled(int major)
    {
        var current = FindJavaBinary(GetInstallDir(major));
        if (current is not null)
            return current;

        // Older layouts from earlier builds.
        foreach (var legacy in GetLegacyInstallDirs(major))
        {
            var found = FindJavaBinary(legacy);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static IEnumerable<string> GetLegacyInstallDirs(int major)
    {
        var launcher = GamePaths.GetLauncherDirectory();
        var mc = GamePaths.GetMinecraftRoot();
        yield return Path.Combine(launcher, "runtime", $"java-{major}");
        yield return Path.Combine(mc, "runtime", $"java-{major}");
        yield return Path.Combine(launcher, "java", $"java-{major}");
    }

    public static async Task<string> EnsureAsync(
        int major,
        HttpClient http,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken)
    {
        if (major < 8)
            major = 8;

        var existing = TryFindInstalled(major);
        if (existing is not null)
            return existing;

        Directory.CreateDirectory(GetJavaRoot());
        var installDir = GetInstallDir(major);
        var staging = installDir + ".partial";
        var zipPath = Path.Combine(GetJavaRoot(), $"java-{major}.zip");

        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            Exception? last = null;
            foreach (var imageType in new[] { "jre", "jdk" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var url = string.Format(AdoptiumLatest, major, imageType);
                    await DownloadFileAsync(http, url, zipPath, byteProgress, cancellationToken)
                        .ConfigureAwait(false);

                    ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
                    if (FindJavaBinary(staging) is null)
                        throw new InvalidOperationException(Loc.Get(LocKeys.Error_JavaExeNotFound));

                    if (Directory.Exists(installDir))
                        Directory.Delete(installDir, recursive: true);

                    Directory.Move(staging, installDir);
                    return FindJavaBinary(installDir)
                        ?? throw new InvalidOperationException(Loc.Get(LocKeys.Error_JavaExeNotFound));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    Debug.WriteLine($"[JavaRuntimeInstaller] {imageType} failed: {ex.Message}");
                    try
                    {
                        if (Directory.Exists(staging))
                        {
                            Directory.Delete(staging, recursive: true);
                            Directory.CreateDirectory(staging);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
                finally
                {
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
                }
            }

            throw new InvalidOperationException(
                Loc.Format(LocKeys.Error_JavaDownloadFailed, major, last?.Message ?? Loc.Get(LocKeys.Error_Unknown)));
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { /* ignore */ }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
            throw;
        }
    }

    public static string PreferJavaw(string javaExePath)
    {
        if (string.IsNullOrWhiteSpace(javaExePath))
            return javaExePath;

        try
        {
            var dir = Path.GetDirectoryName(javaExePath);
            if (string.IsNullOrEmpty(dir))
                return javaExePath;

            var javaw = Path.Combine(dir, "javaw.exe");
            if (File.Exists(javaw))
                return javaw;
        }
        catch
        {
            // ignore
        }

        return javaExePath;
    }

    private static string? FindJavaBinary(string root)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            // Prefer shallow bin\javaw.exe (typical Temurin layout: jdk-21.x\bin\...)
            foreach (var name in new[] { "javaw.exe", "java.exe" })
            {
                foreach (var path in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                {
                    var parent = Path.GetDirectoryName(path);
                    if (parent is not null &&
                        parent.EndsWith("bin", StringComparison.OrdinalIgnoreCase))
                        return PreferJavaw(path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JavaRuntimeInstaller] FindJavaBinary: {ex.Message}");
        }

        return null;
    }

    private static async Task DownloadFileAsync(
        HttpClient http,
        string url,
        string destPath,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken)
    {
        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var dst = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None, 80 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[80 * 1024];
        long progressed = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progressed += read;
            if (total > 0)
                byteProgress?.Report(new ByteProgressInfo(progressed, total));
        }
    }
}
