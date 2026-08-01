using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Ardel.Launcher.Helpers;

namespace Ardel.Launcher.Models;

/// <summary>One Mod catalog hit shown in the download Mod list.</summary>
public sealed partial class ModProjectItem : ObservableObject
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SourceLabel { get; init; }
    public string? IconUrl { get; init; }

    [ObservableProperty]
    private Uri? _iconUri;

    public bool HasIcon => IconUri is not null;
    public ImageSource? IconImage => CatalogIconCache.Get(IconUri, decodePixels: 64);
    public long Downloads { get; init; }
    public string DownloadsLabel { get; init; } = string.Empty;
    public string VersionsLabel { get; init; } = string.Empty;
    public string LoadersLabel { get; init; } = string.Empty;
    public bool HasVersions => !string.IsNullOrEmpty(VersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);

    partial void OnIconUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(IconImage));
    }
}
