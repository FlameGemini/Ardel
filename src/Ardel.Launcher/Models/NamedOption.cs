using CommunityToolkit.Mvvm.ComponentModel;

namespace Ardel.Launcher.Models;

/// <summary>
/// Combo option with a stable <see cref="Id"/> (not localized) and localized <see cref="Name"/>.
/// </summary>
public sealed partial class NamedOption : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    public override string ToString() => Name;
}
