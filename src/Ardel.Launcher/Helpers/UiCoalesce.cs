using Microsoft.UI.Dispatching;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Collapses high-frequency UI property refresh into one dispatcher tick.
/// </summary>
public sealed class UiCoalesce
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _flush;
    private int _pending;

    public UiCoalesce(DispatcherQueue dispatcher, Action flush)
    {
        _dispatcher = dispatcher;
        _flush = flush;
    }

    public void Schedule()
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0)
            return;

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                Interlocked.Exchange(ref _pending, 0);
                _flush();
            }))
        {
            Interlocked.Exchange(ref _pending, 0);
        }
    }

    public void FlushNow()
    {
        Interlocked.Exchange(ref _pending, 0);
        _flush();
    }
}
