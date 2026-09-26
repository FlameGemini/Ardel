using System.Diagnostics;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services.CrashAnalysis;

/// <summary>Collect → rules → present model.</summary>
public static class CrashAnalyzer
{
    public static CrashPresentModel Analyze(
        CrashAnalysisRequest request,
        LauncherSettings settings,
        FactBag? facts = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        facts ??= CrashEvidenceCollector.Collect(
            request.VersionId,
            request.GameDirectory,
            request.SessionStartedAt);
        var match = CrashRuleEngine.Run(facts);
        return CrashPresentBuilder.Build(match, facts, settings);
    }

    public static async Task MaybeAnalyzeAndPresentAsync(
        CrashAnalysisRequest request,
        LauncherSettings settings,
        Microsoft.UI.Xaml.XamlRoot? xamlRoot,
        FactBag? precollectedFacts = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        if (!CrashExitGate.ShouldAnalyze(request.ExitKind, settings))
            return;

        CrashPresentModel model;
        try
        {
            model = await Task.Run(() => Analyze(request, settings, precollectedFacts)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrashAnalyzer] failed: {ex}");
            // Never swallow — still show Unknown so the user can open logs / ChatGPT.
            model = CrashPresentBuilder.Build(null, precollectedFacts ?? new FactBag
            {
                LogsFolderPath = string.IsNullOrWhiteSpace(request.GameDirectory)
                    ? string.Empty
                    : Path.Combine(
                        GamePaths.GetVersionInstanceDirectory(request.VersionId, request.GameDirectory),
                        "logs")
            }, settings);
        }

        var autoOpened = false;
        if (settings.CrashAnalysisAutoOpenLogs)
        {
            try
            {
                OpenLogsFolder(model.LogsFolderPath);
                autoOpened = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CrashAnalyzer] open logs: {ex.Message}");
            }
        }

        model = new CrashPresentModel
        {
            Tier = model.Tier,
            RuleId = model.RuleId,
            Title = model.Title,
            Explain = model.Explain,
            Solution = model.Solution,
            EvidenceLines = model.EvidenceLines,
            Mods = model.Mods,
            Keywords = model.Keywords,
            LogsFolderPath = model.LogsFolderPath,
            AutoOpenedLogs = autoOpened,
            ShowConfidence = model.ShowConfidence,
            ExpandEvidence = model.ExpandEvidence,
            ConfidenceLabel = model.ConfidenceLabel,
            ReportDescription = model.ReportDescription
        };

        if (xamlRoot is null)
            return;

        await Views.CrashAnalysisDialog.ShowAsync(xamlRoot, model).ConfigureAwait(true);
    }

    public static void OpenLogsFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!Directory.Exists(path))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Copies a real evidence pack to the clipboard (when available), then opens ChatGPT.
    /// Never invents crash text — empty pack still opens a blank chat.
    /// </summary>
    public static void OpenChatGpt(CrashPresentModel? model = null)
    {
        try
        {
            if (model is not null)
            {
                var pack = BuildChatPaste(model);
                if (!string.IsNullOrWhiteSpace(pack))
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    data.SetText(pack);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrashAnalyzer] clipboard: {ex.Message}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "https://chatgpt.com",
            UseShellExecute = true
        });
    }

    private static string BuildChatPaste(CrashPresentModel model)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Minecraft crash context from Ardel (copied from local files — verify yourself):");
        if (!string.IsNullOrWhiteSpace(model.Title))
            sb.AppendLine("Title: " + model.Title);
        if (!string.IsNullOrWhiteSpace(model.RuleId))
            sb.AppendLine("Matched pattern id: " + model.RuleId + " (log pattern only, not a confirmed root cause)");
        if (!string.IsNullOrWhiteSpace(model.ReportDescription))
            sb.AppendLine("Description: " + model.ReportDescription);
        if (model.EvidenceLines.Count > 0)
        {
            sb.AppendLine("Evidence:");
            foreach (var line in model.EvidenceLines.Take(12))
                sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }
}

internal static class CrashPresentBuilder
{
    public static CrashPresentModel Build(CrashMatch? match, FactBag facts, LauncherSettings settings)
    {
        var logs = facts.LogsFolderPath;
        if (match is null)
            return BuildUnknown(facts, settings, logs);

        var titleKey = $"Crash_{match.RuleId}_Title";
        var explainKey = $"Crash_{match.RuleId}_Explain";
        var solutionKey = $"Crash_{match.RuleId}_Solution";
        var title = Loc.Get(titleKey);
        var explain = Loc.Get(explainKey);
        var solution = Loc.Get(solutionKey);
        if (title == titleKey || explain == explainKey || solution == solutionKey)
            return BuildUnknown(facts, settings, logs);

        var tier = match.Phase == CrashPhase.Fatal ? CrashTier.Known : CrashTier.Suspected;
        var mods = MergeMods(match, facts);
        var evidence = BuildEvidence(facts, match.Evidence);
        var solutionText = match.RuleId == "R034"
            ? PreferLoaderSolution(solution, facts)
            : solution;

        // Honesty: Suspected is a log-pattern hint, never a verdict.
        if (tier == CrashTier.Suspected)
            explain = Loc.Get(LocKeys.Crash_Usr_NotCertain) + "\n\n" + explain;

        return new CrashPresentModel
        {
            Tier = tier,
            RuleId = match.RuleId,
            Title = title,
            Explain = explain,
            Solution = solutionText,
            EvidenceLines = evidence,
            Mods = mods,
            Keywords = Array.Empty<string>(),
            LogsFolderPath = logs,
            // Never paint a second "confidence" line — the badge already states match strength.
            ShowConfidence = false,
            ExpandEvidence = evidence.Count > 0 || settings.CrashAnalysisVerboseDebug,
            ConfidenceLabel = null,
            ReportDescription = NullIfEmpty(facts.Description)
        };
    }

    private static CrashPresentModel BuildUnknown(FactBag facts, LauncherSettings settings, string logs)
    {
        var evidence = BuildUnknownEvidence(facts);
        var mods = string.IsNullOrWhiteSpace(facts.CaughtExceptionFromMod)
            ? Array.Empty<string>()
            : new[] { facts.CaughtExceptionFromMod };
        var solution = PreferLoaderSolution(Loc.Get(LocKeys.Crash_Unknown_Solution), facts);
        var explain = evidence.Count > 0
            ? Loc.Get(LocKeys.Crash_Unknown_Explain)
            : Loc.Get(LocKeys.Crash_Unknown_ExplainEmpty);
        return new CrashPresentModel
        {
            Tier = CrashTier.Unknown,
            RuleId = null,
            Title = Loc.Get(LocKeys.Crash_Unknown_Title),
            Explain = explain,
            Solution = solution,
            EvidenceLines = evidence,
            Mods = mods,
            Keywords = Array.Empty<string>(),
            LogsFolderPath = logs,
            ShowConfidence = false,
            ExpandEvidence = evidence.Count > 0 || settings.CrashAnalysisVerboseDebug,
            ReportDescription = NullIfEmpty(facts.Description)
        };
    }

    /// <summary>When Fabric/Quilt printed a real solution block, surface it ahead of the generic tip.</summary>
    private static string PreferLoaderSolution(string fallback, FactBag facts)
    {
        if (facts.LoaderSolutionLines.Count == 0)
            return fallback;

        var body = string.Join("\n", facts.LoaderSolutionLines.Take(6));
        if (string.IsNullOrWhiteSpace(body))
            return fallback;
        return body + "\n\n" + fallback;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static IReadOnlyList<string> MergeMods(CrashMatch match, FactBag facts)
    {
        // Prefer what the engine already attached (owned-only).
        if (match.Mods.Count > 0)
            return match.Mods.Take(8).ToList();

        if (!string.IsNullOrWhiteSpace(facts.CaughtExceptionFromMod))
            return [facts.CaughtExceptionFromMod];

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> BuildUnknownEvidence(FactBag facts)
    {
        var lines = new List<string>();

        void Add(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            var t = line.Trim();
            if (t.Length > 280)
                t = t[..277] + "...";
            if (!lines.Contains(t, StringComparer.Ordinal))
                lines.Add(t);
        }

        if (!string.IsNullOrWhiteSpace(facts.Description))
            Add("Description: " + facts.Description.Trim());
        Add(facts.ExceptionLine);

        if (!string.IsNullOrWhiteSpace(facts.CaughtExceptionFromMod))
            Add("Caught exception from " + facts.CaughtExceptionFromMod);

        // First non-empty crash-report lines (real file only — never invent).
        foreach (var raw in facts.CrashReportLines.Take(40))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("Minecraft Version", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("---- Minecraft Crash Report", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Time:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                Add(line);
                continue;
            }

            if (line.Contains("Exception", StringComparison.Ordinal) ||
                line.Contains("Error", StringComparison.Ordinal) ||
                line.StartsWith("at ", StringComparison.Ordinal))
            {
                Add(line);
            }

            if (lines.Count >= 8)
                break;
        }

        if (lines.Count == 0 && facts.HasHsErr)
        {
            foreach (var raw in facts.HsErrLines.Take(20))
            {
                Add(raw);
                if (lines.Count >= 6)
                    break;
            }
        }

        if (lines.Count == 0 && facts.RecentLogLines.Count > 0)
        {
            foreach (var raw in facts.RecentLogLines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(8))
            {
                Add(raw);
                if (lines.Count >= 8)
                    break;
            }
        }

        // Loader tips belong in Solution via PreferLoaderSolution — not Evidence.
        return lines;
    }

    private static IReadOnlyList<string> BuildEvidence(FactBag facts, string? primary)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(facts.Description))
            lines.Add("Description: " + facts.Description.Trim());

        if (!string.IsNullOrWhiteSpace(primary))
        {
            var hit = primary.Trim();
            if (!lines.Contains(hit, StringComparer.Ordinal))
                lines.Add(hit);

            var targetLines = facts.CrashReportLines.Count > 0
                ? facts.CrashReportLines
                : (facts.RecentLogLines.Count > 0 ? facts.RecentLogLines : facts.HsErrLines);

            if (targetLines.Count > 0)
            {
                var idx = FindLineIndex(targetLines, hit);
                if (idx >= 0)
                {
                    var from = Math.Max(0, idx - 1);
                    var to = Math.Min(targetLines.Count - 1, idx + 3);
                    for (var i = from; i <= to; i++)
                    {
                        var line = targetLines[i].Trim();
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        if (lines.Contains(line, StringComparer.Ordinal))
                            continue;
                        lines.Add(line);
                        if (lines.Count >= 8)
                            break;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(facts.ExceptionLine) &&
            !lines.Any(l => l.Contains(facts.ExceptionLine, StringComparison.OrdinalIgnoreCase)))
            lines.Add(facts.ExceptionLine);

        // Loader tips belong in Solution, not Evidence.
        return lines;
    }

    private static int FindLineIndex(IReadOnlyList<string> lines, string hit)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Contains(hit, StringComparison.OrdinalIgnoreCase))
                return i;

            var prefixLen = Math.Min(48, hit.Length);
            if (prefixLen >= 12 &&
                line.Contains(hit[..prefixLen], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
