namespace Ardel.Launcher.Models;

/// <summary>
/// Combo option with a stable <see cref="Id"/> (not localized) and localized <see cref="Name"/>.
/// </summary>
public sealed class NamedOption
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    public override string ToString() => Name;
}
