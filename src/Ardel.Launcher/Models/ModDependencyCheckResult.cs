namespace Ardel.Launcher.Models;

public sealed class ModDependencyCheckResult
{
    public required IReadOnlyList<ModDependencyItem> Missing { get; init; }
    public required IReadOnlyList<ModDependencyRef> MissingRefs { get; init; }
}
