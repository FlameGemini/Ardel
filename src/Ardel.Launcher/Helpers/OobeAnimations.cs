using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Ardel.Launcher.Helpers;

internal static class OobeAnimations
{
    private const double StepFadeMs = 200;
    private const double StaggerMs = 400;

    public static void ResetVisual(UIElement element)
    {
        element.Opacity = 1;
        if (element is not FrameworkElement fe)
            return;

        fe.RenderTransform = null;
    }

    public static void FadeIn(UIElement element, double durationMs = StepFadeMs)
    {
        element.Opacity = 0;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    /// <summary>Brief opacity dip then fade-in for OOBE step body swaps.</summary>
    public static void CrossfadeIn(UIElement element, double durationMs = StepFadeMs)
    {
        element.Opacity = 0.08;
        var animation = new DoubleAnimation
        {
            From = 0.08,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    public static void SlideInFromLeft(UIElement element, double offset = 36, double durationMs = 480)
    {
        if (element is not FrameworkElement fe)
            return;

        fe.Opacity = 0;
        var transform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = -offset };
        fe.RenderTransform = transform;

        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation
        {
            From = -offset,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(opacity, fe);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    public static void Float(UIElement element, double amplitude = 6, double durationMs = 2200)
    {
        if (element is not FrameworkElement fe)
            return;

        var transform = new Microsoft.UI.Xaml.Media.TranslateTransform();
        fe.RenderTransform = transform;

        var up = new DoubleAnimation
        {
            From = 0,
            To = -amplitude,
            Duration = TimeSpan.FromMilliseconds(durationMs / 2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(up, transform);
        Storyboard.SetTargetProperty(up, "Y");
        storyboard.Children.Add(up);
        storyboard.Begin();
    }

    public static void PopIn(UIElement element, int delayMs = 0)
    {
        if (element is not FrameworkElement fe)
            return;

        fe.Opacity = 0;
        var scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 0.82, ScaleY = 0.82 };
        fe.RenderTransform = scale;
        fe.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };
        var scaleX = new DoubleAnimation
        {
            From = 0.82,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };
        var scaleY = new DoubleAnimation
        {
            From = 0.82,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(opacity, fe);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(scaleX, scale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, scale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    public static void FadeInLogo(UIElement element)
    {
        element.Opacity = 0;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    public static async Task RunTutorialHighlightAsync(IReadOnlyList<FrameworkElement> icons, CancellationToken cancellationToken = default)
    {
        if (icons.Count == 0)
            return;

        foreach (var icon in icons)
        {
            icon.Opacity = 0.35;
            if (icon.RenderTransform is not Microsoft.UI.Xaml.Media.ScaleTransform scale)
            {
                scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1, ScaleY = 1 };
                icon.RenderTransform = scale;
                icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }
            else
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }

        for (var i = 0; i < icons.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var icon = icons[i];
            HighlightIcon(icon);
            if (i < icons.Count - 1)
                await Task.Delay((int)StaggerMs, cancellationToken).ConfigureAwait(true);
        }
    }

    private static void HighlightIcon(FrameworkElement icon)
    {
        icon.Opacity = 1;
        if (icon.RenderTransform is not Microsoft.UI.Xaml.Media.ScaleTransform scale)
        {
            scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1, ScaleY = 1 };
            icon.RenderTransform = scale;
            icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }

        var pulseX = new DoubleAnimation
        {
            To = 1.12,
            Duration = TimeSpan.FromMilliseconds(280),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var pulseY = new DoubleAnimation
        {
            To = 1.12,
            Duration = TimeSpan.FromMilliseconds(280),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(pulseX, scale);
        Storyboard.SetTargetProperty(pulseX, "ScaleX");
        Storyboard.SetTarget(pulseY, scale);
        Storyboard.SetTargetProperty(pulseY, "ScaleY");
        storyboard.Children.Add(pulseX);
        storyboard.Children.Add(pulseY);
        storyboard.Begin();
    }

    public static void RunWelcomeTransition(FrameworkElement welcomePanel)
    {
        // SplitPanel visibility is toggled by IsSplitLayout; only fade the welcome body.
        welcomePanel.Opacity = 0;
        FadeIn(welcomePanel, 420);
    }
}
