namespace Ardel.Launcher.Models;

/// <summary>Offline skin injection for launch (authlib-injector + local Yggdrasil).</summary>
public sealed record OfflineSkinLaunchOptions(
    string PlayerUuid,
    string PlayerName,
    string SkinPngPath,
    bool SlimArms);
