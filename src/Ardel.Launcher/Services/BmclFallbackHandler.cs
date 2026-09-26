namespace Ardel.Launcher.Services;

/// <summary>
/// Tries the official CDN first; on timeout / rate-limit / 5xx, rewrites to BMCLAPI
/// and latches subsequent requests to the mirror for this process.
/// </summary>
public sealed class BmclFallbackHandler : DelegatingHandler
{
    private static int _latchedToBmcl;

    public BmclFallbackHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    public static bool IsLatchedToBmcl => Volatile.Read(ref _latchedToBmcl) != 0;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _latchedToBmcl) != 0)
        {
            RewriteInPlace(request);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        HttpRequestMessage? official = null;
        HttpResponseMessage? response = null;
        try
        {
            official = await CloneRequestAsync(request).ConfigureAwait(false);
            response = await base.SendAsync(official, cancellationToken).ConfigureAwait(false);
            if (!ShouldFallback(response))
                return response;

            response.Dispose();
            response = null;
        }
        catch (Exception ex) when (IsTransientNetwork(ex))
        {
            // fall through to BMCL
        }
        finally
        {
            official?.Dispose();
        }

        Volatile.Write(ref _latchedToBmcl, 1);
        RewriteInPlace(request);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldFallback(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        return code is 408 or 429 or 500 or 502 or 503 or 504;
    }

    private static bool IsTransientNetwork(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
                return true;
        }

        return false;
    }

    private static void RewriteInPlace(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
            return;
        var rewritten = BmclApiMirrorHandler.Rewrite(request.RequestUri.AbsoluteUri);
        if (!string.Equals(rewritten, request.RequestUri.AbsoluteUri, StringComparison.Ordinal))
            request.RequestUri = new Uri(rewritten);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
