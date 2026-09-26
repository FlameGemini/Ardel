namespace Ardel.Launcher.ViewModels;

public sealed class OobeTutorialStepItem
{
    public int Index { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string IconGlyph { get; init; }

    public bool IsLast { get; init; }

    public bool ShowConnector => !IsLast;
}
