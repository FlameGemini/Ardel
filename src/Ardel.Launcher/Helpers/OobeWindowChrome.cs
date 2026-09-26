using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Ardel.Launcher.Helpers;

internal static class OobeWindowChrome
{
    private static bool? _wasMinimizable;
    private static bool? _wasMaximizable;

    public static void Apply(Window window)
    {
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        _wasMinimizable ??= presenter.IsMinimizable;
        _wasMaximizable ??= presenter.IsMaximizable;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
    }

    public static void Restore(Window window)
    {
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        if (_wasMinimizable is bool min)
            presenter.IsMinimizable = min;
        if (_wasMaximizable is bool max)
            presenter.IsMaximizable = max;

        _wasMinimizable = null;
        _wasMaximizable = null;
    }
}
