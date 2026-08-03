namespace Ardel.Launcher.Models;

public enum AccountKind
{
    Offline = 0,
    Microsoft = 1
}

/// <summary>Classic = Steve wide arms; Slim = Alex thin arms.</summary>
public enum SkinArmModel
{
    Classic = 0,
    Slim = 1
}

/// <summary>Skins are stored separately for offline vs Microsoft accounts.</summary>
public enum SkinLibraryKind
{
    Offline = 0,
    Microsoft = 1
}
