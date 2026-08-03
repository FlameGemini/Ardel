namespace Ardel.Launcher.Models;

public sealed class AccountRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AccountKind Kind { get; set; } = AccountKind.Offline;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Offline: derived from name. Microsoft: filled when auth lands.</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Optional skin from the matching library (offline↔offline, microsoft↔microsoft).</summary>
    public string? SkinId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
