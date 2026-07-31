using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

public enum VersionKind
{
    Vanilla,
    Fabric,
    Forge,
    NeoForge,
    OptiFine,
    Custom
}

/// <summary>
/// UI-facing Minecraft version entry.
/// </summary>
public sealed class GameVersionItem : INotifyPropertyChanged
{
    private int? _officialJavaMajor;

    public required string Id { get; init; }
    public string Type { get; init; } = "release";
    public VersionKind Kind { get; init; } = VersionKind.Vanilla;
    public bool IsInstalled { get; set; }
    public DateTimeOffset? ReleaseTime { get; init; }

    /// <summary>Official client JSON URL from version_manifest (may be empty for local-only rows).</summary>
    public string? MetadataUrl { get; init; }

    /// <summary>
    /// <c>javaVersion.majorVersion</c> from official metadata. Null until resolved.
    /// </summary>
    public int? OfficialJavaMajor
    {
        get => _officialJavaMajor;
        set
        {
            if (_officialJavaMajor == value)
                return;
            _officialJavaMajor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RequiredJavaMajor));
            OnPropertyChanged(nameof(JavaRequirementLabel));
        }
    }

    public string DisplayName => Kind switch
    {
        VersionKind.Fabric => Loc.Format(LocKeys.Version_Fabric, Id),
        VersionKind.Forge => Loc.Format(LocKeys.Version_Forge, Id),
        VersionKind.NeoForge => Loc.Format(LocKeys.Version_NeoForge, Id),
        VersionKind.OptiFine => Loc.Format(LocKeys.Version_OptiFine, Id),
        VersionKind.Custom => Id,
        _ => Id
    };

    /// <summary>Short loader / OptiFine / vanilla label for instance lists.</summary>
    public string KindLabel => Kind switch
    {
        VersionKind.Fabric => Loc.Get(LocKeys.Install_LoaderFabric),
        VersionKind.Forge => Loc.Get(LocKeys.Install_LoaderForge),
        VersionKind.NeoForge => Loc.Get(LocKeys.Install_LoaderNeoForge),
        VersionKind.OptiFine => Loc.Get(LocKeys.Install_LoaderOptiFine),
        VersionKind.Custom => Loc.Get(LocKeys.Version_Custom),
        _ => Loc.Get(LocKeys.Version_Vanilla)
    };

    /// <summary>Resolved official major, or 0 while still loading.</summary>
    public int RequiredJavaMajor => OfficialJavaMajor ?? 0;

    /// <summary>Compact badge text shown next to version ids in the download list.</summary>
    public string JavaRequirementLabel
    {
        get
        {
            if (OfficialJavaMajor is int official and > 0)
                return Loc.Format(LocKeys.Download_JavaTag, official);

            // Heuristic only — never block the Download list on per-version HTTP.
            return Loc.Format(LocKeys.Download_JavaTag, Helpers.JavaLocator.GetRequiredJavaMajor(Id));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => DisplayName;
}
