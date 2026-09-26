using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.ViewModels;

public partial class CapeItemViewModel : ObservableObject
{
    public string? Id { get; init; }
    public string Alias { get; init; } = string.Empty;
    public string? Url { get; init; }

    [ObservableProperty]
    private BitmapImage? _displayImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentlyActiveVisibility))]
    [NotifyPropertyChangedFor(nameof(ActiveBorderBrush))]
    [NotifyPropertyChangedFor(nameof(ActiveBorderThickness))]
    private bool _isCurrentlyActive;

    public Visibility IsCurrentlyActiveVisibility => IsCurrentlyActive ? Visibility.Visible : Visibility.Collapsed;
    public string ActiveBadgeText => Loc.Get(LocKeys.Account_CapeActive);

    public Brush ActiveBorderBrush => IsCurrentlyActive
        ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
        : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

    public Thickness ActiveBorderThickness => IsCurrentlyActive ? new Thickness(2) : new Thickness(1);
}
