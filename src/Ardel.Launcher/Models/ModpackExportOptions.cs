namespace Ardel.Launcher.Models;

/// <summary>What to include when exporting an instance as <c>.mrpack</c>.</summary>
public sealed class ModpackExportOptions
{
    public bool IncludeMods { get; init; } = true;
    public bool IncludeConfig { get; init; } = true;
    public bool IncludeResourcePacks { get; init; } = true;
    public bool IncludeShaderPacks { get; init; } = true;
    public bool IncludeDatapacks { get; init; } = true;
    public bool IncludeOptions { get; init; } = true;
    public bool IncludeSaves { get; init; }
    public bool IncludeScreenshots { get; init; }

    /// <summary>
    /// When true, jars resolvable on Modrinth go into <c>files[]</c> with download URLs
    /// (small pack); unknowns stay in <c>overrides/</c>.
    /// </summary>
    public bool PreferThinPack { get; init; } = true;
}
