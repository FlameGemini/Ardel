namespace CmlLib.Core.Installer.Forge.Versions;

/// <summary>
/// Intercepts Forge adfoc.us redirect hrefs and unwraps the real maven download URL.
/// </summary>
public static class AdfocUrlInterceptor
{
    public static string? Unwrap(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (url.IndexOf("adfoc.us", StringComparison.OrdinalIgnoreCase) < 0)
            return url;

        const string marker = "url=";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return url;

        var target = Uri.UnescapeDataString(url.Substring(idx + marker.Length));
        var amp = target.IndexOf('&');
        if (amp >= 0)
            target = target.Substring(0, amp);

        return string.IsNullOrWhiteSpace(target) ? url : target;
    }
}
