using System.Reflection;

namespace Ardel.Launcher.Helpers;

/// <summary>Assembly metadata shown on the About page.</summary>
public static class AboutMetadata
{
    public const string SourceRepositoryUrl = "https://github.com/FlameGemini/Ardel";
    public const string LicenseUrl = "https://opensource.org/license/osl-3-0";

    public static string ResolveVersionNumber()
    {
        var asm = typeof(AboutMetadata).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "?";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            info = info[..plus];
        return info;
    }

    public static string ResolveCopyright() =>
        typeof(AboutMetadata).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? string.Empty;
}
