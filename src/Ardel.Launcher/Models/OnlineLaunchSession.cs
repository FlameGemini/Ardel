namespace Ardel.Launcher.Models;

/// <summary>Online (Microsoft) session passed into the launch pipeline without leaking CmlLib types at the UI boundary.</summary>
public sealed record OnlineLaunchSession(
    string Username,
    string Uuid,
    string AccessToken);
