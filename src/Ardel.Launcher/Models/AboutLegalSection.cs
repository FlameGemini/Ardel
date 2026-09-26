namespace Ardel.Launcher.Models;

public sealed class AboutLegalSection
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
}
