using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

/// <summary>
/// Discovered JDK / JRE installation.
/// </summary>
public sealed class JavaInstallation
{
    public required string JavaExePath { get; init; }
    public required int MajorVersion { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? Loc.Format(LocKeys.Java_NamedWithPath, MajorVersion, JavaExePath)
            : DisplayName;
}
