using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// In-window startup cover on MainWindow (never a second Window).
/// Hide via Visibility/Opacity — do not tear ProgressBar out of the tree
/// while IsIndeterminate is running (WASDK 0xC000027B stowed crashes).
/// </summary>
internal static class StartupSplash
{
    private static readonly Stopwatch ShownAt = new();
    private static Grid? _overlay;
    private static ProgressBar? _progressBar;
    private static StartupSplashOptions _options = new();

    public static bool IsVisible => _overlay is not null;

    public static void Show(MainWindow window, string? theme, StartupSplashOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _options = options ?? new StartupSplashOptions();
        if (!_options.Enabled)
            return;

        theme = string.IsNullOrWhiteSpace(theme) ? "Default" : theme.Trim();
        var palette = ResolvePalette(theme);
        RemoveOverlay(window);

        var children = new List<UIElement>();

        if (_options.ShowBrandName)
        {
            children.Add(new TextBlock
            {
                Text = Loc.Get(LocKeys.Brand_Name),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(palette.Ink),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        if (_options.ShowProgressBar)
        {
            _progressBar = new ProgressBar
            {
                Width = 280,
                Height = 4,
                IsIndeterminate = true,
                Margin = new Thickness(0, children.Count > 0 ? 28 : 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(palette.Accent),
                Background = new SolidColorBrush(palette.Track)
            };
            children.Add(_progressBar);
        }
        else
        {
            _progressBar = null;
        }

        if (children.Count == 0)
        {
            children.Add(new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(palette.Accent),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var child in children)
            stack.Children.Add(child);

        _overlay = new Grid
        {
            Background = new SolidColorBrush(palette.Canvas),
            RequestedTheme = palette.ElementTheme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            Children = { stack }
        };
        Grid.SetRowSpan(_overlay, 2);
        Canvas.SetZIndex(_overlay, 1000);

        window.RootGrid.Children.Add(_overlay);
        ShownAt.Restart();
    }

    public static async Task CloseWhenReadyAsync(MainWindow? window, int? durationMs = null)
    {
        if (_overlay is null)
            return;

        var target = Math.Clamp(
            durationMs ?? _options.DurationMs,
            StartupSplashOptions.MinDurationMs,
            StartupSplashOptions.MaxDurationMs);
        var remain = target - ShownAt.ElapsedMilliseconds;
        // Do not capture the UI sync context — a congested dispatcher must not
        // extend the splash past the configured minimum display time.
        if (remain > 0)
            await Task.Delay((int)remain).ConfigureAwait(false);

        await EnqueueHideAsync(window).ConfigureAwait(false);
    }

    private static Task EnqueueHideAsync(MainWindow? window)
    {
        var dq = window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dq is null)
        {
            Hide(window);
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                try
                {
                    Hide(window);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            Hide(window);
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    public static void Hide(MainWindow? window)
    {
        if (_overlay is null)
            return;

        try
        {
            if (_progressBar is not null)
                _progressBar.IsIndeterminate = false;

            _overlay.IsHitTestVisible = false;
            _overlay.Opacity = 0;
            _overlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Splash] Hide failed: {ex}");
            StartupClock.Mark($"Splash Hide failed: {ex.Message}");
        }
    }

    public static async Task PreviewAsync(MainWindow window, string? theme, StartupSplashOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        Show(window, theme, options);
        await CloseWhenReadyAsync(window, options.DurationMs).ConfigureAwait(true);
    }

    private static void RemoveOverlay(MainWindow window)
    {
        if (_overlay is null)
            return;

        try
        {
            if (_progressBar is not null)
                _progressBar.IsIndeterminate = false;

            window.RootGrid.Children.Remove(_overlay);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Splash] RemoveOverlay failed: {ex}");
        }
        finally
        {
            _overlay = null;
            _progressBar = null;
        }
    }

    private readonly record struct SplashPalette(
        ElementTheme ElementTheme,
        Windows.UI.Color Canvas,
        Windows.UI.Color Ink,
        Windows.UI.Color Mute,
        Windows.UI.Color Accent,
        Windows.UI.Color Track);

    private static SplashPalette ResolvePalette(string theme)
    {
        theme = AppThemeController.NormalizeThemeCode(theme);
        return theme switch
        {
            "Light" => new(
                ElementTheme.Light,
                ColorHelper.FromArgb(255, 245, 247, 248),
                ColorHelper.FromArgb(255, 28, 32, 36),
                ColorHelper.FromArgb(255, 96, 111, 123),
                ColorHelper.FromArgb(255, 0, 120, 212),
                ColorHelper.FromArgb(40, 0, 0, 0)),
            "Dark" => new(
                ElementTheme.Dark,
                ColorHelper.FromArgb(255, 18, 21, 24),
                ColorHelper.FromArgb(255, 232, 236, 239),
                ColorHelper.FromArgb(255, 154, 162, 171),
                ColorHelper.FromArgb(255, 122, 137, 150),
                ColorHelper.FromArgb(40, 255, 255, 255)),
            "Sakura" => new(
                ElementTheme.Light,
                ColorHelper.FromArgb(255, 255, 228, 240),
                ColorHelper.FromArgb(255, 88, 24, 56),
                ColorHelper.FromArgb(255, 168, 84, 120),
                ColorHelper.FromArgb(255, 232, 64, 150),
                ColorHelper.FromArgb(48, 232, 64, 150)),
            "Samoyed" => new(
                ElementTheme.Light,
                ColorHelper.FromArgb(255, 245, 247, 250),
                ColorHelper.FromArgb(255, 28, 42, 56),
                ColorHelper.FromArgb(255, 96, 115, 134),
                ColorHelper.FromArgb(255, 74, 105, 132),
                ColorHelper.FromArgb(40, 0, 0, 0)),
            "Sweden" => new(
                ElementTheme.Light,
                ColorHelper.FromArgb(255, 245, 248, 252),
                ColorHelper.FromArgb(255, 18, 48, 78),
                ColorHelper.FromArgb(255, 90, 122, 148),
                ColorHelper.FromArgb(255, 0, 106, 167),
                ColorHelper.FromArgb(40, 0, 106, 167)),
            _ when AppThemeController.ResolveSystemIsDark(null) => new(
                ElementTheme.Default,
                ColorHelper.FromArgb(255, 18, 21, 24),
                ColorHelper.FromArgb(255, 232, 236, 239),
                ColorHelper.FromArgb(255, 154, 162, 171),
                ColorHelper.FromArgb(255, 122, 137, 150),
                ColorHelper.FromArgb(40, 255, 255, 255)),
            _ => new(
                ElementTheme.Default,
                ColorHelper.FromArgb(255, 245, 247, 248),
                ColorHelper.FromArgb(255, 28, 32, 36),
                ColorHelper.FromArgb(255, 96, 111, 123),
                ColorHelper.FromArgb(255, 0, 120, 212),
                ColorHelper.FromArgb(40, 0, 0, 0))
        };
    }
}
