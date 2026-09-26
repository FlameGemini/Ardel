namespace Ardel.Launcher.Models;

/// <summary>Editable world fields written back to <c>level.dat</c>.</summary>
public sealed class SaveWorldPropertiesEdit
{
    public required string LevelName { get; init; }
    public required int GameType { get; init; }
    public required int Difficulty { get; init; }
    public required bool AllowCommands { get; init; }
}
