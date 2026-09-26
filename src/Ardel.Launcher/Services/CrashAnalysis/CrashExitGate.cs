using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services.CrashAnalysis;

/// <summary>Decides whether exit should run the crash analysis engine.</summary>
public static class CrashExitGate
{
    public static bool ShouldAnalyze(CrashExitKind kind, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.CrashAnalysisEnabled)
            return false;

        return kind switch
        {
            CrashExitKind.NormalQuit => false,
            CrashExitKind.ArdelForceKill => settings.CrashAnalysisOnForceKill,
            CrashExitKind.CrashLike => true,
            _ => false
        };
    }

    /// <summary>
    /// Conservative classification:
    /// 1. Real on-disk crash-report / hs_err artifact -> CrashLike.
    /// 2. User clicked force-kill -> ArdelForceKill.
    /// 3. Exit code 0 (and no crash artifact) -> NormalQuit (guaranteed quiet on graceful close).
    /// 4. Any non-zero exit code (e.g. exit code 1 or abort codes) -> CrashLike (prevent silent swallow).
    /// </summary>
    public static CrashExitKind ClassifyExit(
        int exitCode,
        bool wasForceKilled,
        FactBag? previewFacts = null)
    {
        var hasArtifact = previewFacts is not null &&
                          (previewFacts.HasCrashReport || previewFacts.HasHsErr);

        // A real artifact from this session beats "user clicked Stop".
        if (hasArtifact)
            return CrashExitKind.CrashLike;

        if (wasForceKilled)
            return CrashExitKind.ArdelForceKill;

        if (exitCode == 0)
            return CrashExitKind.NormalQuit;

        return CrashExitKind.CrashLike;
    }

    public static bool IsNativeOrAbortExitCode(int exitCode)
    {
        var u = unchecked((uint)exitCode);
        return u is 0xC0000005 // ACCESS_VIOLATION
            or 0xC0000409 // STACK_BUFFER_OVERRUN
            or 0xC00000FD // STACK_OVERFLOW
            or 0x80000003; // BREAKPOINT (rare JVM abort)
    }
}
