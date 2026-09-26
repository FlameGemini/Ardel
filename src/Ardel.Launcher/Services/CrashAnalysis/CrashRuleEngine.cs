namespace Ardel.Launcher.Services.CrashAnalysis;

internal delegate string? CrashRuleMatcher(FactBag facts);

internal sealed class CrashRuleDefinition
{
    public required string Id { get; init; }
    public required string Cause { get; init; }
    public required CrashPhase Phase { get; init; }
    public bool Stop { get; init; } = true;
    public required CrashRuleMatcher Match { get; init; }
}

internal sealed class UsrRuleDefinition
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required Func<FactBag, CrashMatch?> Match { get; init; }
}

/// <summary>
/// Trust-first & conservative diagnosis engine:
/// Evaluates fatal/primary rules against crash-report, hs_err, and fresh session logs.
/// When no hard rule matches, evaluates soft USR rules to provide suspected hints.
/// </summary>
public static class CrashRuleEngine
{
    public static CrashMatch? Run(FactBag facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // If no crash report, no hs_err, and no fresh log output, cannot diagnose.
        if (!facts.HasCrashReport && !facts.HasHsErr && facts.RecentLogLines.Count == 0)
            return null;

        var bestFatal = BestMatch(facts, CrashPhase.Fatal);
        if (bestFatal is not null)
            return AttachOwnedMods(bestFatal, facts);

        var bestPrimary = BestMatch(facts, CrashPhase.Primary);
        if (bestPrimary is not null)
            return AttachOwnedMods(bestPrimary, facts);

        // Soft USR rules when no hard R rule matched.
        try
        {
            foreach (var usr in CrashUsrCatalog.Rules)
            {
                var match = usr.Match(facts);
                if (match is not null)
                    return AttachOwnedMods(match, facts);
            }
        }
        catch
        {
            // Defensive: USR rules failure must never break diagnosis
        }

        return null;
    }

    private static CrashMatch? BestMatch(FactBag facts, CrashPhase phase)
    {
        CrashMatch? best = null;
        foreach (var rule in CrashRuleCatalog.Rules)
        {
            if (rule.Phase != phase)
                continue;

            var needle = rule.Match(facts);
            if (needle is null)
                continue;

            var evidence = CrashEvidenceCollector.FindEvidenceLine(facts, needle) ?? needle;
            var match = new CrashMatch
            {
                RuleId = rule.Id,
                Cause = rule.Cause,
                Phase = rule.Phase,
                Evidence = evidence,
                Specificity = needle.Length
            };

            if (best is null ||
                match.Specificity > best.Specificity ||
                (match.Specificity == best.Specificity &&
                 string.CompareOrdinal(match.RuleId, best.RuleId) < 0))
                best = match;
        }

        return best;
    }

    private static CrashMatch AttachOwnedMods(CrashMatch match, FactBag facts)
    {
        if (string.IsNullOrWhiteSpace(facts.CaughtExceptionFromMod))
            return match;

        return new CrashMatch
        {
            RuleId = match.RuleId,
            Cause = match.Cause,
            Phase = match.Phase,
            Evidence = match.Evidence,
            Specificity = match.Specificity,
            Mods = [facts.CaughtExceptionFromMod],
            Keywords = match.Keywords
        };
    }
}

internal static class CrashRuleMatchers
{
    /// <summary>Crash-report, hs_err, and fresh session latest.log tail.</summary>
    private static string Corpus(FactBag facts)
    {
        var parts = new List<string>(3);
        if (facts.HasCrashReport && !string.IsNullOrWhiteSpace(facts.CrashReport))
            parts.Add(facts.CrashReport);
        if (facts.HasHsErr && !string.IsNullOrWhiteSpace(facts.HsErr))
            parts.Add(facts.HsErr);
        if (facts.RecentLogLines.Count > 0)
            parts.Add(string.Join('\n', facts.RecentLogLines));

        return parts.Count > 0 ? string.Join('\n', parts) : string.Empty;
    }

    public static string? Contains(FactBag facts, params string[] needles)
    {
        var corpus = Corpus(facts);
        if (corpus.Length == 0)
            return null;

        string? best = null;
        foreach (var needle in needles)
        {
            if (string.IsNullOrEmpty(needle) || needle.Length < 12)
                continue;
            if (!corpus.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || needle.Length > best.Length)
                best = needle;
        }

        return best;
    }

    public static string? ContainsAll(FactBag facts, params string[] needles)
    {
        var corpus = Corpus(facts);
        if (corpus.Length == 0)
            return null;

        foreach (var needle in needles)
        {
            if (string.IsNullOrEmpty(needle) || needle.Length < 6)
                return null;
            if (!corpus.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return needles.OrderByDescending(n => n.Length).First();
    }

    public static string? ContainsPreferCrashReport(FactBag facts, params string[] needles) =>
        Contains(facts, needles);

    public static string? ContainsInCrashOrLog(FactBag facts, params string[] needles) =>
        Contains(facts, needles);

    public static string? NamedModEvidence(FactBag facts, params string[] structuralNeedles)
    {
        var corpus = Corpus(facts);
        if (corpus.Length == 0)
            return null;

        var owned =
            corpus.Contains("Caught exception from", StringComparison.OrdinalIgnoreCase) ||
            corpus.Contains("LoaderExceptionModCrash", StringComparison.OrdinalIgnoreCase) ||
            corpus.Contains("Mixin apply for mod", StringComparison.OrdinalIgnoreCase);

        if (!owned)
            return null;

        string? best = null;
        foreach (var needle in structuralNeedles)
        {
            if (string.IsNullOrEmpty(needle) || needle.Length < 10)
                continue;
            if (needle.Contains("provided by", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!corpus.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || needle.Length > best.Length)
                best = needle;
        }

        return best;
    }
}
