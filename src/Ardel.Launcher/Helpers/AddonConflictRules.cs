using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Addon conflict rules: Forge / Fabric / NeoForge / OptiFine are mutually exclusive
/// (enforced by a single radio selection in the install dialog).
/// </summary>
public static class AddonConflictRules
{
    public static string DisplayName(ModLoaderKind kind) => kind switch
    {
        ModLoaderKind.Fabric => Loc.Get(LocKeys.Install_LoaderFabric),
        ModLoaderKind.Forge => Loc.Get(LocKeys.Install_LoaderForge),
        ModLoaderKind.NeoForge => Loc.Get(LocKeys.Install_LoaderNeoForge),
        ModLoaderKind.OptiFine => Loc.Get(LocKeys.Install_LoaderOptiFine),
        _ => Loc.Get(LocKeys.Install_LoaderNone)
    };

    /// <summary>Final validation before install (single radio choice is always exclusive).</summary>
    public static string? ValidateInstall(ModLoaderKind loader)
    {
        _ = loader;
        return null;
    }
}
