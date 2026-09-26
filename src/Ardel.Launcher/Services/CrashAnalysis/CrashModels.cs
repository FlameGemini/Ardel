namespace Ardel.Launcher.Services.CrashAnalysis;

public enum CrashTier
{
    Known,
    Suspected,
    Unknown
}

public enum CrashPhase
{
    Fatal,
    Primary
}

public enum CrashExitKind
{
    NormalQuit,
    ArdelForceKill,
    CrashLike
}

/// <summary>Presentation model for <see cref="Views.CrashAnalysisDialog"/>.</summary>
public sealed class CrashPresentModel
{
    public required CrashTier Tier { get; init; }
    public string? RuleId { get; init; }
    public required string Title { get; init; }
    public required string Explain { get; init; }
    public required string Solution { get; init; }
    public IReadOnlyList<string> EvidenceLines { get; init; } = [];
    public IReadOnlyList<string> Mods { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public required string LogsFolderPath { get; init; }
    public bool AutoOpenedLogs { get; init; }
    public bool ShowConfidence { get; init; }
    public bool ExpandEvidence { get; init; }
    public string? ConfidenceLabel { get; init; }
    /// <summary>Real crash-report Description line when present (never invented).</summary>
    public string? ReportDescription { get; init; }
}

public sealed class CrashAnalysisRequest
{
    public required string VersionId { get; init; }
    public required string GameDirectory { get; init; }
    public int ExitCode { get; init; }
    public CrashExitKind ExitKind { get; init; }
    public DateTimeOffset ExitedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SessionStartedAt { get; init; }
}

public sealed class CrashMatch
{
    public required string RuleId { get; init; }
    public required string Cause { get; init; }
    public required CrashPhase Phase { get; init; }
    public required string Evidence { get; init; }
    public IReadOnlyList<string> Mods { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Longer matched needle ⇒ more specific ⇒ preferred.</summary>
    public int Specificity { get; init; }
}

public sealed class FactBag
{
    public string Combined { get; init; } = string.Empty;

    /// <summary>
    /// Text rules should search: fresh crash-report + hs_err + recent log tail only.
    /// </summary>
    public string MatchCorpus { get; init; } = string.Empty;

    public string CrashReport { get; init; } = string.Empty;
    public string LatestLog { get; init; } = string.Empty;
    public string HsErr { get; init; } = string.Empty;
    public IReadOnlyList<string> CrashReportLines { get; init; } = [];
    public IReadOnlyList<string> LatestLogLines { get; init; } = [];
    public IReadOnlyList<string> RecentLogLines { get; init; } = [];
    public IReadOnlyList<string> HsErrLines { get; init; } = [];
    public IReadOnlyList<string> SuspectedModsFromReport { get; init; } = [];
    public IReadOnlyList<string> StackKeywords { get; init; } = [];
    public IReadOnlyList<string> StackMappedMods { get; init; } = [];
    public IReadOnlyList<string> InstanceModNames { get; init; } = [];

    /// <summary>Parsed from real crash-report "Description:" line.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>First exception-looking line from the real crash-report.</summary>
    public string ExceptionLine { get; init; } = string.Empty;

    /// <summary>Mod id after "Caught exception from" when present in the report.</summary>
    public string CaughtExceptionFromMod { get; init; } = string.Empty;

    /// <summary>Lines following Fabric/Quilt "A potential solution has been determined".</summary>
    public IReadOnlyList<string> LoaderSolutionLines { get; init; } = [];

    public bool HasCrashReport { get; init; }
    public bool HasHsErr { get; init; }
    public bool OutputVeryShort { get; init; }
    public string LogsFolderPath { get; init; } = string.Empty;
    public string CrashReportsFolderPath { get; init; } = string.Empty;

    public string EffectiveMatchText =>
        !string.IsNullOrEmpty(MatchCorpus) ? MatchCorpus :
        !string.IsNullOrEmpty(Combined) ? Combined :
        !string.IsNullOrEmpty(CrashReport) ? CrashReport :
        (RecentLogLines.Count > 0 ? string.Join('\n', RecentLogLines) : string.Empty);
}
