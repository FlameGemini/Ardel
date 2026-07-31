namespace Ardel.Launcher.Services;

/// <summary>
/// Rewrites Mojang / Forge / Fabric CDN URLs to BMCLAPI hosts.
/// </summary>
public sealed class BmclApiMirrorHandler : DelegatingHandler
{
    public const string BmclApiHost = "bmclapi2.bangbang93.com";
    public const string BmclApiBase = "https://" + BmclApiHost;

    public BmclApiMirrorHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var rewritten = Rewrite(request.RequestUri.AbsoluteUri);
            if (!string.Equals(rewritten, request.RequestUri.AbsoluteUri, StringComparison.Ordinal))
                request.RequestUri = new Uri(rewritten);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Map official CDN hosts to BMCLAPI.</summary>
    public static string Rewrite(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
            return originalUrl;

        if (originalUrl.Contains(BmclApiHost, StringComparison.OrdinalIgnoreCase))
            return originalUrl;

        return originalUrl
            .Replace("https://launchermeta.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace("http://launchermeta.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace("https://launcher.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace("http://launcher.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace("https://piston-meta.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace("https://piston-data.mojang.com", BmclApiBase, StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://resources.download.minecraft.net",
                $"{BmclApiBase}/assets",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://libraries.minecraft.net",
                $"{BmclApiBase}/libraries",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://files.minecraftforge.net/maven",
                $"{BmclApiBase}/maven",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://maven.minecraftforge.net",
                $"{BmclApiBase}/maven",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://maven.neoforged.net/releases",
                $"{BmclApiBase}/maven",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://meta.fabricmc.net",
                $"{BmclApiBase}/fabric-meta",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "https://maven.fabricmc.net",
                $"{BmclApiBase}/maven",
                StringComparison.OrdinalIgnoreCase);
    }
}

