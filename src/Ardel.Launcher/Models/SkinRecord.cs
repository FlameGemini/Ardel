namespace Ardel.Launcher.Models;

public sealed class SkinRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public SkinLibraryKind Library { get; set; } = SkinLibraryKind.Offline;
    public SkinArmModel ArmModel { get; set; } = SkinArmModel.Classic;

    /// <summary>File name under the skins root (PNG).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>True for launcher-provided Steve/Alex presets (not user-deletable).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Fixed custom slot (Custom 1 / Custom 2); not freely deletable.</summary>
    public bool IsCustomSlot { get; set; }

    /// <summary>False for empty custom slots that still need a PNG import.</summary>
    public bool IsConfigured { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
