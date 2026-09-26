namespace Ardel.Launcher.Models;

/// <summary>Launcher-wide default memory policy.</summary>
public enum DefaultMemoryMode
{
    /// <summary>Fixed maximum heap from <see cref="LauncherSettings.MaxRamMb"/>.</summary>
    Custom = 0,

    /// <summary>Compute heap from current system memory at launch.</summary>
    Dynamic = 1
}

/// <summary>Per-instance memory policy.</summary>
public enum InstanceMemoryMode
{
    /// <summary>Use the launcher default memory policy.</summary>
    FollowDefault = 0,

    /// <summary>Fixed maximum heap from <see cref="InstanceSettings.MaxRamMb"/>.</summary>
    Custom = 1,

    /// <summary>Compute heap from current system memory at launch.</summary>
    Dynamic = 2
}
