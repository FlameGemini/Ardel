using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

/// <summary>One collapsible acknowledgments group on the About page.</summary>
public sealed class AboutCreditGroup
{
    public required string HeadingLocKey { get; init; }
    public required IReadOnlyList<AboutCreditItem> Items { get; init; }

    public string Heading => Loc.Get(HeadingLocKey);
}
