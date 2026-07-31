using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Coalesces high-frequency file progress (~2.5 Hz). Status-only updates flush immediately
/// so long metadata / gate waits do not look frozen on the last label.
/// </summary>
public sealed class CoalescedUiProgress
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly Action<string, double, bool> _apply;
    private readonly object _gate = new();
    private string _name = string.Empty;
    private int _progressed;
    private int _total;
    private double _progress;
    private bool _indeterminate = true;
    private int _generation;
    private int _scheduled;
    private long _lastFlushTicks;

    public CoalescedUiProgress(DispatcherQueue dispatcher, Action<string, double, bool> apply)
    {
        _dispatcher = dispatcher;
        _apply = apply;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Flush();
    }

    public void Report(string status, double progress, bool indeterminate)
    {
        lock (_gate)
        {
            _name = status;
            _total = 0;
            SetProgressMonotonic(progress);
            _indeterminate = indeterminate && _progress <= 0;
            _generation++;
        }

        ScheduleImmediate();
    }

    /// <summary>Status-only (no bar numbers) — flush right away.</summary>
    public void ReportStatus(string status)
    {
        if (string.IsNullOrEmpty(status))
            return;

        lock (_gate)
        {
            _name = status;
            _total = 0;
            _indeterminate = true;
            _generation++;
        }

        ScheduleImmediate();
    }

    /// <summary>File-count progress drives the bar (stable). Status shows current file.</summary>
    public void ReportFile(string name, int progressed, int total)
    {
        if (total <= 0)
        {
            ReportStatus(name);
            return;
        }

        lock (_gate)
        {
            _name = name;
            _progressed = progressed;
            _total = total;
            _indeterminate = false;
            SetProgressMonotonic(progressed * 100.0 / total);
            _generation++;
        }

        Schedule();
    }

    public void ReportBytes(long progressed, long total)
    {
        _ = progressed;
        _ = total;
    }

    private void SetProgressMonotonic(double value)
    {
        var next = Math.Clamp(value, 0, 100);
        if (next > _progress)
            _progress = next;
    }

    private void ScheduleImmediate()
    {
        _timer.Stop();
        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Flush))
        {
            // Fall back to coalesced path if enqueue fails.
            Interlocked.Exchange(ref _scheduled, 0);
            Schedule();
        }
    }

    private void Schedule()
    {
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
            return;

        var elapsedMs = (Stopwatch.GetTimestamp() - Volatile.Read(ref _lastFlushTicks)) *
                        1000.0 / Stopwatch.Frequency;
        var waitMs = Math.Max(0, MinInterval.TotalMilliseconds - elapsedMs);

        if (waitMs <= 1)
        {
            if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Flush))
                Interlocked.Exchange(ref _scheduled, 0);
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(waitMs);
        _timer.Start();
    }

    private void Flush()
    {
        int gen;
        try
        {
            string status;
            double progress;
            bool indeterminate;

            lock (_gate)
            {
                status = _total > 0
                    ? Loc.Format(LocKeys.Progress_FileCount, _name, _progressed, _total)
                    : _name;
                progress = _progress;
                indeterminate = _indeterminate;
                gen = _generation;
            }

            _apply(status, progress, indeterminate);
            Volatile.Write(ref _lastFlushTicks, Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoalescedUiProgress] Apply failed: {ex.Message}");
            gen = Volatile.Read(ref _generation);
        }
        finally
        {
            Interlocked.Exchange(ref _scheduled, 0);
        }

        if (Volatile.Read(ref _generation) != gen)
            Schedule();
    }
}

/// <summary>
/// <see cref="IProgress{T}"/> that does not capture <see cref="SynchronizationContext"/>.
/// </summary>
public sealed class DirectProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public DirectProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
