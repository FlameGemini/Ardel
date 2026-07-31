using CommunityToolkit.Mvvm.ComponentModel;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

public enum ModDependencyKind
{
    Required,
    Optional
}

/// <summary>Raw dependency reference attached to a Mod file/version.</summary>
public sealed class ModDependencyRef
{
    public required string ProjectId { get; init; }
    public string? VersionId { get; init; }
    public required string SourceId { get; init; }
    public required ModDependencyKind Kind { get; init; }
}

/// <summary>Resolved dependency row for the Mod detail page.</summary>
public sealed partial class ModDependencyItem : ObservableObject
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string VersionsLabel { get; init; }
    public required string LoadersLabel { get; init; }
    public required ModDependencyKind Kind { get; init; }
    public string? IconUrl { get; init; }

    [ObservableProperty]
    private Uri? _iconUri;

    public bool HasIcon => IconUri is not null;
    public bool HasVersions => !string.IsNullOrEmpty(VersionsLabel);
    public bool HasLoaders => !string.IsNullOrEmpty(LoadersLabel);

    public string KindLabel => Kind == ModDependencyKind.Required
        ? Loc.Get(LocKeys.Mod_DependencyRequired)
        : Loc.Get(LocKeys.Mod_DependencyOptional);

    partial void OnIconUriChanged(Uri? value) => OnPropertyChanged(nameof(HasIcon));

    public ModProjectItem ToProjectItem() => new()
    {
        Id = Id,
        SourceId = SourceId,
        Title = Title,
        Description = string.Empty,
        SourceLabel = string.Empty,
        IconUrl = IconUrl,
        IconUri = IconUri,
        Downloads = 0,
        DownloadsLabel = string.Empty,
        VersionsLabel = VersionsLabel,
        LoadersLabel = LoadersLabel
    };
}
