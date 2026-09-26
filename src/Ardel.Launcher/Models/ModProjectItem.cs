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
    public string SourceLabel
    {
        get => _sourceLabel;
        set => SetProperty(ref _sourceLabel, value);
    }

    private string _sourceLabel = string.Empty;
    public string? IconUrl { get; init; }
    public string? WebPageUrl { get; init; }
    public string? Slug { get; init; }

    [ObservableProperty]
    private Uri? _iconUri;

    public bool HasIcon => IconUri is not null;
    public ImageSource? IconImage => CatalogIconCache.Get(IconUri, decodePixels: 64);
    public long Downloads { get; init; }
    public string DownloadsLabel
    {
        get => _downloadsLabel;
        set => SetProperty(ref _downloadsLabel, value);
    }

    private string _downloadsLabel = string.Empty;
    public DateTimeOffset? Updated { get; init; }
    public string UpdatedLabel
    {
        get => _updatedLabel;
        set => SetProperty(ref _updatedLabel, value);
    }

    private string _updatedLabel = string.Empty;
    public bool HasUpdated => !string.IsNullOrEmpty(UpdatedLabel);
    public string VersionsLabel { get; init; } = string.Empty;
    public string LoadersLabel { get; init; } = string.Empty;
    public bool HasVersions => !string.IsNullOrEmpty(VersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);

    partial void OnIconUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(IconImage));
    }

    /// <summary>Refresh source/download labels after <see cref="Loc.SetLanguage"/>.</summary>
    public void NotifyLocalization()
    {
        SourceLabel = CatalogCopy.SourceLabel(SourceId);
        DownloadsLabel = CatalogCopy.FormatDownloads(Downloads);
        UpdatedLabel = CatalogCopy.FormatUpdated(Updated);
    }
}
