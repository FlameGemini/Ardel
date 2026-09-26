using System.Diagnostics;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// High-precision, zero-allocation watchdog for monitoring interaction execution latency.
/// Measures UI thread execution and alerts whenever an interaction exceeds its latency budget (default: 5.0ms).
/// </summary>
public static class InteractionWatchdog
{
    private const double DefaultBudgetMs = 5.0;

    /// <summary>
    /// Begins profiling an interaction block.
    /// Usage: <c>using var _ = InteractionWatchdog.Profile("NavView_SelectionChanged");</c>
    /// </summary>
    [DebuggerStepThrough]
    public static Scope Profile(string interactionName, double budgetMs = DefaultBudgetMs) =>
        new(interactionName, budgetMs);

    public readonly ref struct Scope
    {
        private readonly string _name;
        private readonly double _budgetMs;
        private readonly long _startTimestamp;

        [DebuggerStepThrough]
        public Scope(string name, double budgetMs)
        {
            _name = name;
            _budgetMs = budgetMs;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        [DebuggerStepThrough]
        public void Dispose()
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
            var elapsedMs = (double)elapsedTicks * 1000.0 / Stopwatch.Frequency;

            if (elapsedMs > _budgetMs)
            {
                Debug.WriteLine($"[PERF-ALERT] ⚠️ Interaction '{_name}' took {elapsedMs:F2}ms (exceeded {_budgetMs:F1}ms budget!)");
            }
            else
            {
                // Optional verbose trace
                // Debug.WriteLine($"[PERF-OK] Interaction '{_name}' finished in {elapsedMs:F2}ms");
            }
        }
    }
}
