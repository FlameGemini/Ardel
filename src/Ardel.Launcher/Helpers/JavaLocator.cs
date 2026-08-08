using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Scans the machine for usable java.exe installations and reports major versions.
/// </summary>
public static partial class JavaLocator
{
    private static readonly ConcurrentDictionary<string, int> VersionCache = new(StringComparer.OrdinalIgnoreCase);

    private static string CachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "java_version_cache.json");

    static JavaLocator()
    {
        try
        {
            var path = CachePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (dict is not null)
                {
                    foreach (var kv in dict)
                    {
                        if (File.Exists(kv.Key))
                            VersionCache[kv.Key] = kv.Value;
                    }
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(VersionCache);
            File.WriteAllText(CachePath, json);
        }
        catch
        {
            // best-effort
        }
    }

    private static readonly string[] CommonRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AdoptOpenJDK"),
        // Microsoft OpenJDK lives under Program Files\Microsoft\jdk-* (never scan all of Microsoft/).
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gradle", "jdks"),
        Path.Combine(GamePaths.GetLauncherDirectory(), "java"),
        // Legacy auto-download roots
        Path.Combine(GamePaths.GetLauncherDirectory(), "runtime"),
        Path.Combine(GamePaths.GetMinecraftRoot(), "runtime"),
    ];

    /// <summary>
    /// Discovers all unique java.exe paths from env, registry, and common folders.
    /// </summary>
    public static IReadOnlyList<JavaInstallation> FindInstallations()
    {
        var found = new Dictionary<string, JavaInstallation>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? javaExe, string source)
        {
            if (string.IsNullOrWhiteSpace(javaExe))
                return;

            try
            {
                javaExe = Path.GetFullPath(javaExe);
            }
            catch
            {
                return;
            }

            if (!File.Exists(javaExe))
                return;

            if (found.ContainsKey(javaExe))
                return;

            try
            {
                var major = GetJavaVersion(javaExe);
                if (major <= 0)
                    return;

                found[javaExe] = new JavaInstallation
                {
                    JavaExePath = javaExe,
                    MajorVersion = major,
                    DisplayName = Loc.Format(LocKeys.Java_NamedWithSource, major, source),
                    Source = source
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JavaLocator] Skip {javaExe}: {ex.Message}");
            }
        }

        // 1) JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            TryAdd(Path.Combine(javaHome, "bin", "java.exe"), Loc.Get(LocKeys.Java_SourceJavaHome));
        }

        // 2) PATH — only real ...\bin\java.exe (skip Oracle javapath stubs that often hang).
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!segment.EndsWith("bin", StringComparison.OrdinalIgnoreCase))
                continue;
            if (segment.Contains("javapath", StringComparison.OrdinalIgnoreCase))
                continue;

            TryAdd(Path.Combine(segment, "java.exe"), Loc.Get(LocKeys.Java_SourcePath));
        }

        // 3) Windows registry (Oracle / Adoptium / Microsoft, etc.)
        foreach (var javaExe in EnumerateRegistryJava())
        {
            TryAdd(javaExe.path, javaExe.source);
        }

        // 4) Common install directories (shallow only — AllDirectories under Program Files freezes the UI).
        foreach (var root in CommonRoots)
            ScanShallowJavaHomes(root, Loc.Get(LocKeys.Java_SourceCommon), TryAdd);

        var microsoftRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft");
        ScanMicrosoftJdks(microsoftRoot, TryAdd);

        return found.Values
            .OrderByDescending(j => j.MajorVersion)
            .ThenBy(j => j.JavaExePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Look for <c>{root}/{home}/bin/java.exe</c> and <c>{root}/bin/java.exe</c> only —
    /// no recursive walk of huge trees like Program Files\Microsoft.
    /// </summary>
    private static void ScanShallowJavaHomes(string root, string source, Action<string?, string> tryAdd)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            tryAdd(Path.Combine(root, "bin", "java.exe"), source);

            foreach (var home in Directory.EnumerateDirectories(root))
                tryAdd(Path.Combine(home, "bin", "java.exe"), Path.GetFileName(root) ?? source);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JavaLocator] Scan failed for {root}: {ex.Message}");
        }
    }

    private static void ScanMicrosoftJdks(string microsoftRoot, Action<string?, string> tryAdd)
    {
        if (!Directory.Exists(microsoftRoot))
            return;

        try
        {
            foreach (var home in Directory.EnumerateDirectories(microsoftRoot, "jdk-*"))
                tryAdd(Path.Combine(home, "bin", "java.exe"), "Microsoft");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JavaLocator] Microsoft JDK scan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the major Java version from <c>java -version</c> output (e.g. 8, 17, 21).
    /// </summary>
    public static int GetJavaVersion(string javaExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(javaExePath);

        if (VersionCache.TryGetValue(javaExePath, out var cached))
            return cached;

        if (!File.Exists(javaExePath))
            throw new FileNotFoundException(Loc.Get(LocKeys.Error_JavaExeNotFound), javaExePath);

        var psi = new ProcessStartInfo
        {
            FileName = javaExePath,
            Arguments = "-version",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(Loc.Format(LocKeys.Error_JavaProcessStart, javaExePath));

        // Avoid ReadToEnd + WaitForExit deadlock on full pipes; enforce a hard timeout.
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(2500))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException(Loc.Get(LocKeys.Error_TimedOut));
        }

        var stderr = stderrTask.GetAwaiter().GetResult();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        var major = ParseMajorVersion(text);
        if (major <= 0)
            throw new InvalidOperationException(
                Loc.Format(LocKeys.Error_JavaVersionParse, Environment.NewLine, text));

        VersionCache[javaExePath] = major;
        SaveCache();
        return major;
    }

    /// <summary>
    /// Picks a Java that satisfies the required major version (exact or newer for modern MC).
    /// </summary>
    public static JavaInstallation? FindBestMatch(int requiredMajor, IEnumerable<JavaInstallation>? candidates = null)
    {
        var list = (candidates ?? FindInstallations()).ToList();
        if (list.Count == 0)
            return null;

        // Prefer exact match, then closest higher version. Never return a too-old Java.
        return list
            .Where(j => j.MajorVersion >= requiredMajor)
            .OrderBy(j => j.MajorVersion)
            .ThenBy(j => j.JavaExePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Heuristic fallback only when official metadata is unavailable.
    /// Prefer <see cref="OfficialJavaRequirements"/>.
    /// </summary>
    public static int GetRequiredJavaMajor(string minecraftVersionId)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersionId))
            return 21;

        // Strip loader prefixes like "fabric-loader-0.16.0-1.21.1" / "1.20.1-forge-47.2.0"
        var match = McVersionRegex().Match(minecraftVersionId);
        if (!match.Success || !Version.TryParse(NormalizeVersion(match.Groups[1].Value), out var ver))
            return 17;

        // Official mapping (simplified):
        // 1.20.5+ → 21, 1.18–1.20.4 → 17, 1.17 → 16, 1.12–1.16 → 8
        if (ver >= new Version(1, 20, 5))
            return 21;
        if (ver >= new Version(1, 18))
            return 17;
        if (ver >= new Version(1, 17))
            return 16;
        return 8;
    }

    private static string NormalizeVersion(string raw)
    {
        var parts = raw.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => string.Join('.', parts.Take(3))
        };
    }

    internal static int ParseMajorVersion(string versionOutput)
    {
        // Examples:
        // java version "1.8.0_402"
        // openjdk version "17.0.11"
        // openjdk version "21.0.3" 2024-04-16
        var m = JavaVersionRegex().Match(versionOutput);
        if (!m.Success)
            return -1;

        var token = m.Groups[1].Value;
        if (token.StartsWith("1.", StringComparison.Ordinal))
        {
            // 1.8.0_xxx → 8
            var legacy = token.Split('.');
            return legacy.Length >= 2 && int.TryParse(legacy[1], out var legacyMajor) ? legacyMajor : -1;
        }

        var majorPart = token.Split('.', '-', '+')[0];
        return int.TryParse(majorPart, out var major) ? major : -1;
    }

    private static IEnumerable<(string path, string source)> EnumerateRegistryJava()
    {
        string[] roots =
        [
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\JRE",
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Eclipse Foundation\JDK",
            @"SOFTWARE\Microsoft\JDK",
            @"SOFTWARE\Azul Systems\Zulu",
            @"SOFTWARE\BellSoft\Liberica"
        ];

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var root in roots)
            {
                using var key = baseKey.OpenSubKey(root);
                if (key is null)
                    continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var home = sub?.GetValue("JavaHome") as string
                               ?? sub?.GetValue("Path") as string
                               ?? sub?.GetValue("InstallationPath") as string;

                    if (string.IsNullOrWhiteSpace(home))
                        continue;

                    yield return (Path.Combine(home, "bin", "java.exe"),
                        Loc.Format(LocKeys.Java_SourceRegistryNamed, view));
                }
            }
        }
    }

    [GeneratedRegex(@"version\s+""(?<v>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaVersionRegex();

    [GeneratedRegex(@"(\d+\.\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex McVersionRegex();
}
