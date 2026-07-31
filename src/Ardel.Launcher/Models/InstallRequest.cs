namespace Ardel.Launcher.Models;

/// <summary>
/// Install addon choice. Forge / Fabric / NeoForge / OptiFine are mutually exclusive.
/// </summary>
public enum ModLoaderKind
{
    None,
    Fabric,
    Forge,
    NeoForge,
    OptiFine
}

/// <summary>
/// One selectable Fabric / Forge / NeoForge / OptiFine build for a Minecraft version.
/// </summary>
public sealed class ModLoaderVersionOption
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsLatest { get; init; }
    public bool IsStable { get; init; }
    public bool IsPreview { get; init; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// User choices from the install options dialog.
/// </summary>
public sealed class InstallRequest
{
    public required string MinecraftVersionId { get; init; }
    public required string CustomVersionName { get; init; }

    /// <summary>Exactly one of None / Fabric / Forge / NeoForge / OptiFine.</summary>
    public ModLoaderKind Loader { get; init; } = ModLoaderKind.None;

    /// <summary>Selected build id for Fabric / Forge / NeoForge / OptiFine.</summary>
    public string? LoaderVersion { get; init; }

    /// <summary>When true and Loader is Fabric, also download Fabric API into mods/.</summary>
    public bool InstallFabricApi { get; init; }
}
