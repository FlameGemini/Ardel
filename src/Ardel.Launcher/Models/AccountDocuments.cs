namespace Ardel.Launcher.Models;

public sealed class AccountsDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string? ActiveAccountId { get; set; }
    public List<AccountRecord> Accounts { get; set; } = [];
}

public sealed class SkinsDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<SkinRecord> Skins { get; set; } = [];
}
