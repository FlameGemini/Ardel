using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Models;

/// <summary>One mod identified inside a modpack version (for the contents page).</summary>
public sealed partial class ModpackContentItem : ObservableObject
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public string? IconUrl { get; init; }
    public string? WebPageUrl { get; init; }
    public string? Slug { get; init; }

    [ObservableProperty]
    private Uri? _iconUri;

    public string SourceLabel => CatalogCopy.SourceLabel(SourceId);

    public bool HasIcon => IconUri is not null;
    public ImageSource? IconImage => CatalogIconCache.Get(IconUri, decodePixels: 48);

    public bool CanOpenWeb => !string.IsNullOrWhiteSpace(WebPageUrl);
    public bool CanOpenInLauncher => !string.IsNullOrWhiteSpace(Id);

    public string OpenSiteLabel =>
        string.Equals(SourceId, ModSearchViewModel.SourceIdModrinth, StringComparison.OrdinalIgnoreCase)
            ? Localization.Loc.Get(Localization.LocKeys.Mod_DetailOpenModrinth)
            : Localization.Loc.Get(Localization.LocKeys.Mod_DetailOpenCurseForge);

    partial void OnIconUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(IconImage));
    }

    public ModProjectItem ToProjectItem() => new()
    {
        Id = Id,
        SourceId = SourceId,
        Title = Title,
        Description = string.Empty,
        SourceLabel = SourceLabel,
        IconUrl = IconUrl,
        IconUri = IconUri,
        WebPageUrl = WebPageUrl,
        Slug = Slug,
        Downloads = 0,
        DownloadsLabel = string.Empty
    };
}

/// <summary>Navigation payload for <see cref="Views.ModpackContentsPage"/>.</summary>
public sealed class ModpackContentsNavArgs
{
    public required string PackTitle { get; init; }
    public required string VersionLabel { get; init; }
    public required IReadOnlyList<ModpackContentItem> Contents { get; init; }

    /// <summary>Pack project so Back from an opened mod can restore the modpack detail.</summary>
    public ModProjectItem? PackProject { get; init; }
}
