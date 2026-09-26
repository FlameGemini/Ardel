using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Helpers;

/// <summary>Catalog UI copy keyed by project family so screens do not always say “Mod”.</summary>
public static class CatalogCopy
{
    public static string SourceLabel(string sourceId) =>
        string.Equals(sourceId, ModSearchViewModel.SourceIdModrinth, StringComparison.OrdinalIgnoreCase)
            ? Loc.Get(LocKeys.Mod_SourceModrinth)
            : string.Equals(sourceId, ModSearchViewModel.SourceIdCurseForge, StringComparison.OrdinalIgnoreCase)
                ? Loc.Get(LocKeys.Mod_SourceCurseForge)
                : sourceId;

    public static string FormatDownloads(long count)
    {
        if (count >= 1_000_000)
            return Loc.Format(LocKeys.Mod_DownloadsMillions, (count / 1_000_000d).ToString("0.#"));
        if (count >= 1_000)
            return Loc.Format(LocKeys.Mod_DownloadsThousands, (count / 1_000d).ToString("0.#"));
        return Loc.Format(LocKeys.Mod_DownloadsExact, count);
    }

    public static string FormatUpdated(DateTimeOffset? updated)
    {
        if (updated is null)
            return string.Empty;

        var utc = updated.Value.ToUniversalTime();
        var delta = DateTimeOffset.UtcNow - utc;
        if (delta.TotalMinutes < 1)
            return Loc.Get(LocKeys.Catalog_UpdatedJustNow);
        if (delta.TotalHours < 1)
            return Loc.Format(LocKeys.Catalog_UpdatedMinutes, Math.Max(1, (int)delta.TotalMinutes));
        if (delta.TotalDays < 1)
            return Loc.Format(LocKeys.Catalog_UpdatedHours, Math.Max(1, (int)delta.TotalHours));
        if (delta.TotalDays < 14)
            return Loc.Format(LocKeys.Catalog_UpdatedDays, Math.Max(1, (int)delta.TotalDays));
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public static string FormatPublished(DateTimeOffset? published) => FormatUpdated(published);

    public static string KindLabel(CatalogProjectKind kind) => kind switch
    {
        CatalogProjectKind.ResourcePack => Loc.Get(LocKeys.Catalog_KindResourcePack),
        CatalogProjectKind.Datapack => Loc.Get(LocKeys.Catalog_KindDatapack),
        CatalogProjectKind.ShaderPack => Loc.Get(LocKeys.Catalog_KindShaderPack),
        CatalogProjectKind.Modpack => Loc.Get(LocKeys.Catalog_KindModpack),
        _ => Loc.Get(LocKeys.Catalog_KindMod)
    };

    public static string SearchHint(CatalogProjectKind kind) =>
        Loc.Format(LocKeys.Catalog_SearchHint, KindLabel(kind));

    public static string SearchEmpty(CatalogProjectKind kind) =>
        Loc.Format(LocKeys.Catalog_SearchEmpty, KindLabel(kind));

    public static string InstallDialogTitle(CatalogProjectKind kind)
    {
        var noun = kind switch
        {
            CatalogProjectKind.ResourcePack => Loc.Get(LocKeys.Download_SectionResourcePack),
            CatalogProjectKind.Datapack => Loc.Get(LocKeys.Download_SectionDatapack),
            CatalogProjectKind.ShaderPack => Loc.Get(LocKeys.Download_SectionShaderPack),
            CatalogProjectKind.Modpack => Loc.Get(LocKeys.Download_SectionModpack),
            _ => Loc.Get(LocKeys.Download_SectionMod)
        };
        return Loc.Format(LocKeys.Catalog_InstallDialogTitle, noun);
    }

    public static string NoCompatible(CatalogProjectKind kind, bool hasAnyInstance)
    {
        if (!hasAnyInstance)
            return Loc.Get(LocKeys.Mod_InstallNoInstances);

        return kind == CatalogProjectKind.Mod
            ? Loc.Get(LocKeys.Mod_InstallNoCompatible)
            : Loc.Get(LocKeys.Catalog_InstallNoCompatiblePack);
    }
}
