using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Ardel.Launcher.Localization;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class DownloadProgressFlyout : UserControl
{
    private static readonly TimeSpan AnimDuration = TimeSpan.FromMilliseconds(220);

    private DownloadViewModel? _viewModel;
    private bool _shown;
    private bool _animating;
    private Storyboard? _activeStoryboard;

    public event EventHandler? OpenDownloadRequested;

    public DownloadProgressFlyout()
    {
        InitializeComponent();
        Opacity = 0;
        Visibility = Visibility.Collapsed;
        RenderTransform = new TranslateTransform { Y = 18 };
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1);
    }

    public void Bind(DownloadViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ActiveDownloads.CollectionChanged -= OnJobsChanged;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ActiveDownloads.CollectionChanged += OnJobsChanged;
        JobsList.ItemsSource = _viewModel.ActiveDownloads;
        SyncVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadViewModel.HasActiveDownloads)
            or nameof(DownloadViewModel.ActiveDownloadCount))
            SyncVisibility();
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SyncVisibility();

    private void SyncVisibility()
    {
        if (_viewModel is null)
            return;

        var count = _viewModel.ActiveDownloads.Count;
        HeaderText.Text = count <= 1
            ? Loc.Get(LocKeys.Download_FlyoutHeader)
            : Loc.Format(LocKeys.Download_FlyoutHeaderCount, count);

        var wantShow = count > 0;
        if (wantShow == _shown && !_animating)
            return;

        if (wantShow)
            AnimateShow();
        else
            AnimateHide();
    }

    private void AnimateShow()
    {
        _shown = true;
        StopActiveStoryboard();

        if (RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform { Y = 18 };
            RenderTransform = transform;
        }

        Visibility = Visibility.Visible;
        _animating = true;

        var sb = new Storyboard();
        sb.Children.Add(CreateDoubleAnimation(this, "Opacity", Opacity, 1, AnimDuration));
        sb.Children.Add(CreateDoubleAnimation(transform, "Y", transform.Y, 0, AnimDuration));
        sb.Completed += (_, _) =>
        {
            _animating = false;
            Opacity = 1;
            transform.Y = 0;
            // If jobs cleared during show animation, hide again.
            if (_viewModel is null || _viewModel.ActiveDownloads.Count == 0)
                AnimateHide();
        };

        _activeStoryboard = sb;
        sb.Begin();
    }

    private void AnimateHide()
    {
        _shown = false;
        if (Visibility != Visibility.Visible)
        {
            Opacity = 0;
            return;
        }

        StopActiveStoryboard();

        if (RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            RenderTransform = transform;
        }

        _animating = true;
        var sb = new Storyboard();
        sb.Children.Add(CreateDoubleAnimation(this, "Opacity", Opacity, 0, AnimDuration));
        sb.Children.Add(CreateDoubleAnimation(transform, "Y", transform.Y, 18, AnimDuration));
        sb.Completed += (_, _) =>
        {
            _animating = false;
            Opacity = 0;
            transform.Y = 18;
            // Only collapse if still empty (a new job may have arrived).
            if (_viewModel is null || _viewModel.ActiveDownloads.Count == 0)
                Visibility = Visibility.Collapsed;
            else
                AnimateShow();
        };

        _activeStoryboard = sb;
        sb.Begin();
    }

    private void StopActiveStoryboard()
    {
        if (_activeStoryboard is null)
            return;

        try { _activeStoryboard.Stop(); } catch { /* ignore */ }
        _activeStoryboard = null;
        _animating = false;
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string propertyPath,
        double from,
        double to,
        TimeSpan duration)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);
        return anim;
    }

    private void RootBorder_Tapped(object sender, TappedRoutedEventArgs e)
    {
        OpenDownloadRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CancelButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }
}
