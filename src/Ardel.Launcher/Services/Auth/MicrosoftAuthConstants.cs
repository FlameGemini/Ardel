namespace Ardel.Launcher.Services.Auth;

/// <summary>Azure app registration for Ardel Desktop Microsoft / Minecraft login.</summary>
internal static class MicrosoftAuthConstants
{
    public const string ClientId = "aa63f134-dc08-47f1-bbd1-f9d961b3ef80";

    /// <summary>
    /// MSAL public-client interactive login uses loopback. In Azure → Authentication,
    /// add platform <c>Mobile and desktop applications</c> with redirect URI
    /// <c>http://localhost</c> (keep the website authcompleted URI if you use it for branding).
    /// </summary>
    public const string LoopbackRedirectUri = "http://localhost";
}
