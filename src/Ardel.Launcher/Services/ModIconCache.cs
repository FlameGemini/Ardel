using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ardel.Launcher.Models;
using Microsoft.UI.Dispatching;

namespace Ardel.Launcher.Services;

/// <summary>
/// Downloads Mod list icons to a local disk cache so the ListView does not hit CDNs on every bind.
/// </summary>
public static class ModIconCache
{
    private const int MaxConcurrentDownloads = 8;

    private static readonly ConcurrentDictionary<string, string> MemoryIndex =
        new(StringComparer.Ordinal);

    private static readonly SemaphoreSlim Gate = new(MaxConcurrentDownloads);

    private static readonly HttpClient Http = CreateHttp();

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ardel",
        "mod-icons");

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = MaxConcurrentDownloads
        })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ardel-Launcher/1.0 (Mod icons)");
        return client;
    }

    /// <summary>Returns a file URI when the icon is already on disk; otherwise null.</summary>
    public static Uri? TryGetCachedUri(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl))
            return null;

        var key = iconUrl.Trim();
        if (MemoryIndex.TryGetValue(key, out var cachedPath) && File.Exists(cachedPath))
            return ToFileUri(cachedPath);

        var path = GetCachePath(key);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            MemoryIndex[key] = path;
            return ToFileUri(path);
        }

        return null;
    }

    /// <summary>
    /// Ensures icons for the given items exist locally and updates <see cref="ModProjectItem.IconUri"/> on the UI thread.
    /// </summary>
    public static Task PrefetchAsync(
        IReadOnlyList<ModProjectItem> items,
        DispatcherQueue dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (items.Count == 0)
            return Task.CompletedTask;

        Directory.CreateDirectory(Root);

        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.IconUrl))
                continue;

            // Already pointing at a local file.
            if (item.IconUri is { IsFile: true })
                continue;

            var cached = TryGetCachedUri(item.IconUrl);
            if (cached is not null)
            {
                var captured = item;
                var uri = cached;
                dispatcher.TryEnqueue(() => captured.IconUri = uri);
                continue;
            }

            tasks.Add(DownloadAndAssignAsync(item, dispatcher, cancellationToken));
        }

        return Task.WhenAll(tasks);
    }

    private static async Task DownloadAndAssignAsync(
        ModProjectItem item,
        DispatcherQueue dispatcher,
        CancellationToken cancellationToken)
    {
        var url = item.IconUrl!.Trim();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var again = TryGetCachedUri(url);
            if (again is not null)
            {
                dispatcher.TryEnqueue(() => item.IconUri = again);
                return;
            }

            var path = GetCachePath(url);
            var temp = path + ".tmp";

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                               temp,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None,
                               81920,
                               FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);

            MemoryIndex[url] = path;
            var uri = ToFileUri(path);
            dispatcher.TryEnqueue(() => item.IconUri = uri);
        }
        catch (OperationCanceledException)
        {
            // Search superseded.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModIconCache] {ex.Message}");
            try
            {
                var temp = GetCachePath(url) + ".tmp";
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string GetCachePath(string iconUrl)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(iconUrl)))
            .ToLowerInvariant();
        var ext = GuessExtension(iconUrl);
        return Path.Combine(Root, hash + ext);
    }

    private static string GuessExtension(string iconUrl)
    {
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
            return ".png";

        var ext = Path.GetExtension(uri.AbsolutePath);
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp")
            return ext.ToLowerInvariant();
        return ".png";
    }

    private static Uri ToFileUri(string path) => new(Path.GetFullPath(path));
}
