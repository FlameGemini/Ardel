namespace Ardel.Launcher.Services;

/// <summary>
/// Rewrites adfoc.us download redirects to the embedded real URL before HTTP runs.
/// Stops Forge installer fetches from hitting adware pages.
/// </summary>
public sealed class AdfocInterceptHandler : DelegatingHandler
{
    public AdfocInterceptHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var original = request.RequestUri.AbsoluteUri;
            var unwrapped = Unwrap(original);
            if (!string.Equals(unwrapped, original, StringComparison.Ordinal))
                request.RequestUri = new Uri(unwrapped);
        }

        return base.SendAsync(request, cancellationToken);
    }

    public static string Unwrap(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (url.IndexOf("adfoc.us", StringComparison.OrdinalIgnoreCase) < 0)
            return url;

        const string marker = "url=";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return url;

        var target = Uri.UnescapeDataString(url[(idx + marker.Length)..]);
        var amp = target.IndexOf('&');
        if (amp >= 0)
            target = target[..amp];

        return string.IsNullOrWhiteSpace(target) ? url : target;
    }
}
