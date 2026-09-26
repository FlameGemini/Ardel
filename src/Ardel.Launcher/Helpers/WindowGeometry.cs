using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Ardel.Launcher.Helpers;

/// <summary>Shared first-launch window size / placement (matches <see cref="MainWindow"/>).</summary>
internal static class WindowGeometry
{
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1200;
    private const int MinWidth = 1280;
    private const int MinHeight = 800;

    public static (SizeInt32 Size, PointInt32 Position) ComputeDefault(DisplayArea display)
    {
        var work = display.WorkArea;
        var width = Math.Clamp((int)(work.Width * 0.92), MinWidth, Math.Max(MinWidth, work.Width - 32));
        var height = Math.Clamp((int)(work.Height * 0.90), MinHeight, Math.Max(MinHeight, work.Height - 32));
        if (width < DefaultWidth && work.Width >= DefaultWidth + 32)
            width = DefaultWidth;
        if (height < DefaultHeight && work.Height >= DefaultHeight + 32)
            height = DefaultHeight;

        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        return (new SizeInt32(width, height), new PointInt32(x, y));
    }

    public static void ApplyPreferredMinimum(OverlappedPresenter presenter)
    {
        presenter.PreferredMinimumWidth = MinWidth;
        presenter.PreferredMinimumHeight = MinHeight;
    }
}
