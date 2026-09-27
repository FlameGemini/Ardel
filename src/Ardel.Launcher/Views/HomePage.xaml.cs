using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class HomePage : Page
{
    private static readonly TimeSpan WeatherFadeDuration = TimeSpan.FromMilliseconds(140);

    private Storyboard? _weatherStoryboard;
    private int _weatherAnimGeneration;

    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        ApplyGreetingTypography();
        ViewModel.WeatherContentAnimator = AnimateWeatherContentAsync;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(ViewModel.WeatherContentAnimator, (Func<Action, Task>)AnimateWeatherContentAsync))
            ViewModel.WeatherContentAnimator = null;
        StopWeatherAnimation();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyGreetingTypography();
        ViewModel.WeatherContentAnimator = AnimateWeatherContentAsync;
        ViewModel.RefreshOnNavigate();
        ViewModel.StartClock();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.StopClock();
        base.OnNavigatedFrom(e);
    }

    private async Task AnimateWeatherContentAsync(Action apply)
    {
        if (WeatherPanel is null || !ShowWeatherPanelReady())
        {
            apply();
            return;
        }

        var generation = ++_weatherAnimGeneration;
        if (_weatherStoryboard is not null)
        {
            try { _weatherStoryboard.Stop(); } catch { /* ignore */ }
            _weatherStoryboard = null;
        }

        await RunOpacityAsync(WeatherPanel, WeatherPanel.Opacity, 0, generation).ConfigureAwait(true);
        if (generation != _weatherAnimGeneration)
            return;

        apply();

        await RunOpacityAsync(WeatherPanel, 0, 1, generation).ConfigureAwait(true);
    }

    private bool ShowWeatherPanelReady() =>
        WeatherPanel.Visibility == Visibility.Visible;

    private async Task RunOpacityAsync(UIElement target, double from, double to, int generation)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(WeatherFadeDuration),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            if (generation == _weatherAnimGeneration)
                target.Opacity = to;
            if (ReferenceEquals(_weatherStoryboard, sb))
                _weatherStoryboard = null;
            tcs.TrySetResult();
        };
        _weatherStoryboard = sb;
        target.Opacity = from;
        sb.Begin();
        await Task.WhenAny(tcs.Task, Task.Delay(400)).ConfigureAwait(true);
        if (generation == _weatherAnimGeneration)
            target.Opacity = to;
    }

    private void StopWeatherAnimation()
    {
        _weatherAnimGeneration++;
        if (_weatherStoryboard is null)
            return;
        try { _weatherStoryboard.Stop(); } catch { /* ignore */ }
        _weatherStoryboard = null;
    }

    private void ApplyGreetingTypography()
    {
        // Always English “Good morning” Latin face (JetBrains Mono), any UI language.
        var font = AppTypography.CreateGreetingFont();
        var weight = AppTypography.GreetingFontWeight();
        var spacing = AppTypography.GreetingCharacterSpacing();

        GreetingText.FontFamily = font;
        GreetingText.FontWeight = weight;
        GreetingText.CharacterSpacing = spacing;
    }

    private void VoiceCard_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("HomePage.VoiceCard_Click");
        VoiceWindowHost.Show();
    }

    private double _lastDotWidth, _lastDotHeight;

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DotMatrixCanvas is null)
            return;

        var w = e.NewSize.Width;
        var h = e.NewSize.Height;
        if (Math.Abs(w - _lastDotWidth) < 10 && Math.Abs(h - _lastDotHeight) < 10)
            return;

        _lastDotWidth = w;
        _lastDotHeight = h;
        RenderDotMatrix(w, h);
    }

    private void RenderDotMatrix(double width, double height)
    {
        DotMatrixCanvas.Children.Clear();
        if (width <= 0 || height <= 0)
            return;

        const double spacing = 28.0;
        const double dotRadius = 1.0;
        var sb = new System.Text.StringBuilder();

        for (double x = spacing; x < width - spacing; x += spacing)
        {
            for (double y = spacing; y < height - spacing; y += spacing)
            {
                sb.Append($"M {x},{y} a {dotRadius} {dotRadius} 0 1 0 0.01 0 ");
            }
        }

        if (sb.Length == 0)
            return;

        var brush = Application.Current.Resources.TryGetValue("ArdelInkBrush", out var b) && b is Microsoft.UI.Xaml.Media.Brush mb
            ? mb
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                typeof(Microsoft.UI.Xaml.Media.Geometry),
                sb.ToString()),
            Fill = brush
        };

        DotMatrixCanvas.Children.Add(path);
    }
}
