using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

/// <summary>One third-party dependency row on the About page.</summary>
public sealed class AboutCreditItem
{
    public required string Name { get; init; }
    public required string SummaryLocKey { get; init; }
    public string? License { get; init; }
    public string? Url { get; init; }

    public string Summary => Loc.Get(SummaryLocKey);

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    public bool HasLicense => !string.IsNullOrWhiteSpace(License);
    public Uri? Link => HasUrl ? new Uri(Url!) : null;
}
