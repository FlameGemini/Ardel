using System.Text;
using System.Text.RegularExpressions;

namespace Ardel.Launcher.Services.CrashAnalysis;

/// <summary>Reads bounded log / crash-report / hs_err slices into a <see cref="FactBag"/>.</summary>
public static class CrashEvidenceCollector
{
    private const int MaxBytesPerFile = 512 * 1024;
    private const int MaxTailLines = 2_000;
    /// <summary>Only the end of latest.log is eligible when no crash-report (stale F3+C etc.).</summary>
    private const int MaxRecentLogLinesForMatch = 400;
    /// <summary>
    /// Tiny clock-skew allowance only. Large negative grace re-binds prior-session crash reports
    /// to a new launch and destroys diagnosis trust.
    /// </summary>
    private static readonly TimeSpan EvidenceGraceBeforeStart = TimeSpan.FromSeconds(5);

    private static readonly Regex SuspectedModLine = new(
        @"Suspected\s+Mods?:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModJarName = new(
        @"([A-Za-z0-9][\w.\-+]*)\.jar",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MixinConfigRegex = new(
        @"(?:config\s*\[|in\s+config\s+\[?)([\w.\-+]+?)\.mixins?\.json\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MixinApplyForModRegex = new(
        @"Mixin\s+apply\s+for\s+mod\s+([\w.\-+]+)\s+failed",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> NoiseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "java", "javax", "jdk", "sun", "com.sun", "minecraft", "net.minecraft",
        "com.mojang", "forge", "neoforge", "fabric", "quilt", "cpw", "org.spongepowered",
        "org.lwjgl", "io.netty", "com.google", "org.apache", "it.unimi", "org.objectweb",
        "transformer", "mixin", "thread", "run", "main", "client", "server"
    };

    public static FactBag Collect(
        string versionId,
        string gameDirectory,
        DateTimeOffset? sessionStartedAtUtc = null)
    {
        var instance = GamePaths.GetVersionInstanceDirectory(versionId, gameDirectory);
        var logsDir = Path.Combine(instance, "logs");
        var crashDir = Path.Combine(instance, "crash-reports");
        // Do not create folders during analysis — empty dirs look fabricated.

        var notBefore = sessionStartedAtUtc is { } started
            ? started - EvidenceGraceBeforeStart
            : (DateTimeOffset?)null;

        var latestLogPath = Path.Combine(logsDir, "latest.log");
        var crashPath = FindNewestFile(crashDir, "crash-*.txt", notBefore)
                        ?? FindNewestFile(crashDir, "*.txt", notBefore);
        var hsErrPath = FindNewestFile(instance, "hs_err_pid*.log", notBefore)
                        ?? FindNewestFile(logsDir, "hs_err_pid*.log", notBefore);

        var crashText = ReadTail(crashPath);
        var logText = ReadTail(latestLogPath);
        var hsText = ReadTail(hsErrPath);

        var crashLines = SplitLines(crashText);
        var logLines = SplitLines(logText);
        var hsLines = SplitLines(hsText);

        // Session-bind latest.log: only use the tail when the file itself was touched this session.
        var logSessionFresh = IsFileFresh(latestLogPath, notBefore);
        var recentLogLines = !logSessionFresh
            ? []
            : logLines.Count <= MaxRecentLogLinesForMatch
                ? logLines
                : logLines.Skip(logLines.Count - MaxRecentLogLinesForMatch).ToList();
        var recentLogText = string.Join('\n', recentLogLines);

        // Rules match fresh crash-report + hs_err + recent session log.
        var matchCorpus = string.Join('\n', new[] { crashText, hsText, recentLogText }.Where(s => s.Length > 0));
        var combined = string.Join('\n', new[] { crashText, recentLogText, hsText }.Where(s => s.Length > 0));

        var modsIndex = IndexInstanceMods(Path.Combine(instance, "mods"));
        var suspected = ParseSuspectedMods(matchCorpus);
        // Soft stacks kept for debug only — never presented as guilt.
        var stackMapped = MapStackToMods(matchCorpus, modsIndex);
        var keywords = ExtractStackKeywords(matchCorpus);
        var headLines = crashLines.Count > 0 ? crashLines : recentLogLines;
        var (description, exceptionLine, caughtFrom) = ParseCrashHead(headLines);
        var resolvedMod = ResolveFaultyMod(caughtFrom, matchCorpus, modsIndex);
        var finalCaughtFrom = !string.IsNullOrWhiteSpace(resolvedMod) ? resolvedMod : caughtFrom;
        if (!string.IsNullOrWhiteSpace(resolvedMod) && !suspected.Contains(resolvedMod, StringComparer.OrdinalIgnoreCase))
            suspected.Insert(0, resolvedMod);

        // Loader solutions from both crash-report and recent session logs.
        var solutionLines = ParseLoaderSolution(crashLines, recentLogLines);

        var totalChars = matchCorpus.Length;
        return new FactBag
        {
            Combined = combined,
            MatchCorpus = matchCorpus,
            CrashReport = crashText,
            LatestLog = logText,
            HsErr = hsText,
            CrashReportLines = crashLines,
            LatestLogLines = logLines,
            RecentLogLines = recentLogLines,
            HsErrLines = hsLines,
            SuspectedModsFromReport = suspected,
            StackKeywords = keywords,
            StackMappedMods = stackMapped,
            InstanceModNames = modsIndex.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Description = description,
            ExceptionLine = exceptionLine,
            CaughtExceptionFromMod = finalCaughtFrom,
            LoaderSolutionLines = solutionLines,
            HasCrashReport = !string.IsNullOrWhiteSpace(crashText),
            HasHsErr = !string.IsNullOrWhiteSpace(hsText),
            OutputVeryShort = totalChars < 400 && string.IsNullOrWhiteSpace(crashText),
            LogsFolderPath = logsDir,
            CrashReportsFolderPath = crashDir
        };
    }

    private static string ResolveFaultyMod(
        string existingCaughtFrom,
        string matchCorpus,
        Dictionary<string, string> modsIndex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(existingCaughtFrom))
            {
                if (modsIndex.TryGetValue(existingCaughtFrom, out var matched))
                    return matched;
                return existingCaughtFrom;
            }

            if (string.IsNullOrEmpty(matchCorpus))
                return string.Empty;

            var applyMatch = MixinApplyForModRegex.Match(matchCorpus);
            if (applyMatch.Success)
            {
                var modId = applyMatch.Groups[1].Value.Trim();
                if (modsIndex.TryGetValue(modId, out var display))
                    return display;
                if (!NoiseKeywords.Contains(modId) && modId.Length >= 2)
                    return modId;
            }

            var configMatch = MixinConfigRegex.Match(matchCorpus);
            if (configMatch.Success)
            {
                var configStem = configMatch.Groups[1].Value.Trim();
                var cleanToken = configStem.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)[0];
                if (modsIndex.TryGetValue(configStem, out var display))
                    return display;
                if (modsIndex.TryGetValue(cleanToken, out var cleanDisplay))
                    return cleanDisplay;
                if (!NoiseKeywords.Contains(cleanToken) && cleanToken.Length >= 3)
                    return cleanToken;
            }
        }
        catch
        {
            // Defensive: resolution failure must never throw
        }

        return string.Empty;
    }

    public static string? FindEvidenceLine(FactBag facts, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return null;

        foreach (var line in facts.CrashReportLines
                     .Concat(facts.HsErrLines)
                     .Concat(facts.RecentLogLines.Count > 0 ? facts.RecentLogLines : facts.LatestLogLines))
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return TrimEvidence(line);
        }

        var corpus = string.IsNullOrEmpty(facts.MatchCorpus) ? facts.Combined : facts.MatchCorpus;
        var idx = corpus.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var start = corpus.LastIndexOf('\n', idx);
        start = start < 0 ? 0 : start + 1;
        var end = corpus.IndexOf('\n', idx);
        if (end < 0)
            end = Math.Min(corpus.Length, idx + 200);
        return TrimEvidence(corpus[start..end]);
    }

    private static string TrimEvidence(string line)
    {
        var t = line.Trim();
        return t.Length <= 280 ? t : t[..277] + "...";
    }

    private static string ReadTail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return string.Empty;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = fs.Length;
            if (length <= 0)
                return string.Empty;

            var take = (int)Math.Min(length, MaxBytesPerFile);
            fs.Seek(-take, SeekOrigin.End);
            var buffer = new byte[take];
            var read = fs.Read(buffer, 0, take);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var lines = SplitLines(text);
            if (lines.Count > MaxTailLines)
                lines = lines.Skip(lines.Count - MaxTailLines).ToList();
            return string.Join('\n', lines);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .ToList();
    }

    private static bool IsFileFresh(string path, DateTimeOffset? notBeforeUtc)
    {
        if (notBeforeUtc is null)
            return true;
        try
        {
            if (!File.Exists(path))
                return false;
            return File.GetLastWriteTimeUtc(path) >= notBeforeUtc.Value.UtcDateTime;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindNewestFile(string directory, string pattern, DateTimeOffset? notBeforeUtc)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;

            var query = Directory.EnumerateFiles(directory, pattern)
                .Select(p => new FileInfo(p));

            if (notBeforeUtc is { } min)
            {
                var minUtc = min.UtcDateTime;
                query = query.Where(f => f.LastWriteTimeUtc >= minUtc);
            }

            return query
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> IndexInstanceMods(string modsDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(modsDir))
                return map;

            foreach (var path in Directory.EnumerateFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly))
            {
                var file = Path.GetFileName(path);
                var stem = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(stem))
                    continue;
                map[stem] = stem;
                var token = stem.Split(['-', '_', ' '], 2, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!string.IsNullOrWhiteSpace(token) && token.Length >= 3)
                    map.TryAdd(token, stem);
            }
        }
        catch
        {
            // ignore
        }

        return map;
    }

    private static (string Description, string ExceptionLine, string CaughtFrom) ParseCrashHead(
        IReadOnlyList<string> crashLines)
    {
        var description = string.Empty;
        var exceptionLine = string.Empty;
        var caughtFrom = string.Empty;

        for (var i = 0; i < crashLines.Count; i++)
        {
            var line = crashLines[i].Trim();
            if (line.Length == 0)
                continue;

            if (description.Length == 0 &&
                line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                description = line["Description:".Length..].Trim();
                if (description.Length == 0 && i + 1 < crashLines.Count)
                    description = crashLines[i + 1].Trim();
                continue;
            }

            if (caughtFrom.Length == 0)
            {
                const string prefix = "Caught exception from ";
                var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    caughtFrom = line[(idx + prefix.Length)..].Trim().TrimEnd('.');
                    // Drop trailing ":" / noise
                    var cut = caughtFrom.IndexOfAny([' ', '\t', '(']);
                    if (cut > 0)
                        caughtFrom = caughtFrom[..cut];
                }
            }

            if (exceptionLine.Length == 0 &&
                (line.Contains("Exception:", StringComparison.Ordinal) ||
                 line.Contains("Error:", StringComparison.Ordinal) ||
                 Regex.IsMatch(line, @"^(?:java|javax|jdk|sun|com|net|org)\.[\w.]+(?:Error|Exception)\b")))
            {
                exceptionLine = line.Length <= 240 ? line : line[..237] + "...";
            }

            if (description.Length > 0 && exceptionLine.Length > 0 && caughtFrom.Length > 0)
                break;
        }

        return (description, exceptionLine, caughtFrom);
    }

    private static List<string> ParseLoaderSolution(
        IReadOnlyList<string> crashLines,
        IReadOnlyList<string> recentLogLines)
    {
        var lines = new List<string>();
        var sources = new[] { crashLines, recentLogLines };
        foreach (var source in sources)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var line = source[i];
                if (!line.Contains("A potential solution has been determined", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("A potential solution has been found", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Capture the next non-empty actionable lines (Fabric prints them after the header).
                for (var j = i; j < Math.Min(source.Count, i + 12); j++)
                {
                    var t = source[j].Trim();
                    if (t.Length == 0)
                        continue;
                    if (t.StartsWith("---", StringComparison.Ordinal) ||
                        t.StartsWith("at ", StringComparison.Ordinal))
                        break;
                    if (t.Length > 280)
                        t = t[..277] + "...";
                    if (!lines.Contains(t, StringComparer.Ordinal))
                        lines.Add(t);
                    if (lines.Count >= 8)
                        return lines;
                }

                if (lines.Count > 0)
                    return lines;
            }
        }

        return lines;
    }

    private static List<string> ParseSuspectedMods(string crashText)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(crashText))
            return list;

        foreach (Match m in SuspectedModLine.Matches(crashText))
        {
            var raw = m.Groups[1].Value.Trim();
            if (raw.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (Match jar in ModJarName.Matches(raw))
            {
                var name = Path.GetFileNameWithoutExtension(jar.Groups[1].Value + ".jar");
                if (!string.IsNullOrWhiteSpace(name) &&
                    !list.Contains(name, StringComparer.OrdinalIgnoreCase))
                    list.Add(name);
            }

            if (list.Count == 0)
            {
                foreach (var part in raw.Split([',', ';', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                        part.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Skip forge "Unknown (foo)" noise unless jar-like
                    if (part.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (part.Length is >= 2 and <= 80 &&
                        !list.Contains(part, StringComparer.OrdinalIgnoreCase))
                        list.Add(part);
                }
            }
        }

        // Fabric / Quilt often: "Mod File: foo-1.0.jar" near the head
        foreach (Match m in Regex.Matches(crashText, @"Mod File:\s*([^\r\n]+)", RegexOptions.IgnoreCase))
        {
            var file = Path.GetFileNameWithoutExtension(m.Groups[1].Value.Trim().Trim('"'));
            if (file.Length is >= 2 and <= 80 &&
                !list.Contains(file, StringComparer.OrdinalIgnoreCase))
                list.Add(file);
        }

        return list.Take(8).ToList();
    }

    private static List<string> MapStackToMods(string combined, Dictionary<string, string> modsIndex)
    {
        if (modsIndex.Count == 0 || string.IsNullOrEmpty(combined))
            return [];

        var hits = new List<string>();
        foreach (var (key, display) in modsIndex)
        {
            if (key.Length < 4)
                continue;
            if (combined.Contains(key, StringComparison.OrdinalIgnoreCase) &&
                !hits.Contains(display, StringComparer.OrdinalIgnoreCase))
                hits.Add(display);
            if (hits.Count >= 5)
                break;
        }

        return hits.Count is >= 1 and <= 5 ? hits : [];
    }

    private static List<string> ExtractStackKeywords(string combined)
    {
        if (string.IsNullOrEmpty(combined))
            return [];

        var keywords = new List<string>();
        foreach (Match m in Regex.Matches(combined, @"\bat\s+([\w.$]+)\("))
        {
            var type = m.Groups[1].Value;
            var simple = type.Contains('.') ? type[(type.LastIndexOf('.') + 1)..] : type;
            if (simple.Length < 4 || NoiseKeywords.Contains(simple))
                continue;
            var root = type.Split('.')[0];
            if (NoiseKeywords.Contains(root))
                continue;
            if (!keywords.Contains(simple, StringComparer.OrdinalIgnoreCase))
                keywords.Add(simple);
            if (keywords.Count >= 8)
                break;
        }

        return keywords;
    }

    /// <summary>
    /// Re-reads evidence a few times so late crash-reports / hs_err can appear after process exit.
    /// </summary>
    public static async Task<FactBag> CollectWithRetryAsync(
        string versionId,
        string gameDirectory,
        DateTimeOffset? sessionStartedAtUtc = null,
        int attempts = 8,
        int delayMs = 400,
        CancellationToken cancellationToken = default)
    {
        FactBag? last = null;
        for (var i = 0; i < Math.Max(1, attempts); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await Task.Run(
                    () => Collect(versionId, gameDirectory, sessionStartedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            if (last.HasCrashReport || last.HasHsErr)
                return last;

            if (i + 1 < attempts)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return last!;
    }
}
