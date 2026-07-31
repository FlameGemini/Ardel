using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Services;

/// <summary>
/// Game-file installer tuned for ~5k Minecraft assets.
/// Avoids CmlLib <see cref="ParallelGameInstaller"/> Dataflow stalls and per-chunk progress.
/// Small assets (sounds etc.) use higher concurrency and are scheduled before large jars
/// so the download tail is not stuck on hundreds of RTT-bound .ogg requests.
/// </summary>
internal sealed class ArdelGameInstaller : IGameInstaller
{
    public const int DownloadConcurrency = 16;
    public const int SmallDownloadConcurrency = 64;
    private const int CheckConcurrency = 64;
    private const int MaxRetries = 8;
    private const int SmallFileBytes = 512 * 1024;
    /// <summary>Assets at or below this size use the high-concurrency path.</summary>
    private const int SmallDownloadBytes = 256 * 1024;
    private const int CopyBufferSize = 64 * 1024;
    private const string TempSuffix = ".ardel-tmp";

    private readonly HttpClient _http;
    private int _running;

    public ArdelGameInstaller(HttpClient httpClient) =>
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async ValueTask Install(
        IEnumerable<GameFile> gameFiles,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_AlreadyInstalling));

        try
        {
            var files = Deduplicate(gameFiles);
            if (files.Count == 0)
                return;

            var total = files.Count;
            fileProgress?.Report(new InstallerProgressChangedEventArgs(
                0, 0, Loc.Format(LocKeys.Download_CheckingFiles, 0, total), InstallerEventType.Done));

            // Split local checks from network so resume doesn't crawl through "already OK" files
            // on the same pool as real downloads.
            var missing = new List<GameFile>(files.Count);
            var present = new List<GameFile>(files.Count);
            var gate = new object();
            var checkedCount = 0;

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = CheckConcurrency,
                    CancellationToken = cancellationToken
                },
                (file, _) =>
                {
                    if (NeedsDownload(file))
                    {
                        lock (gate) missing.Add(file);
                    }
                    else
                    {
                        lock (gate) present.Add(file);
                    }

                    var n = Interlocked.Increment(ref checkedCount);
                    // Status-only (TotalTasks=0) so the bar stays indeterminate during the scan.
                    if (n == 1 || n == total || n % 128 == 0)
                    {
                        fileProgress?.Report(new InstallerProgressChangedEventArgs(
                            0, 0, Loc.Format(LocKeys.Download_CheckingFiles, n, total),
                            InstallerEventType.Done));
                    }

                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);

            long totalBytes = 0;
            foreach (var f in files)
                totalBytes += Math.Max(0L, f.Size);

            if (missing.Count > 0)
            {
                fileProgress?.Report(new InstallerProgressChangedEventArgs(
                    0, 0, Loc.Format(LocKeys.Download_DownloadingCount, 0, missing.Count),
                    InstallerEventType.Done));
            }

            var counters = new ProgressCounters();

            // Fast path: already-on-disk files (run UpdateTask e.g. legacy asset copies).
            await Parallel.ForEachAsync(
                present,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = CheckConcurrency,
                    CancellationToken = cancellationToken
                },
                async (file, ct) =>
                {
                    await file.ExecuteUpdateTask(ct).ConfigureAwait(false);
                    FinishOne(file, counters, total, totalBytes,
                        fileProgress, byteProgress, forceProgress: false);
                }).ConfigureAwait(false);

            // Tiny assets first at high concurrency — otherwise they pile up at the end and
            // crawl (one RTT each) after large jars have occupied the pool / warmed rate limits.
            var small = new List<GameFile>(missing.Count);
            var large = new List<GameFile>();
            foreach (var file in missing)
            {
                if (IsSmallDownload(file))
                    small.Add(file);
                else
                    large.Add(file);
            }

            await DownloadBatchAsync(
                    small, SmallDownloadConcurrency, total, totalBytes, counters,
                    fileProgress, byteProgress, cancellationToken)
                .ConfigureAwait(false);

            await DownloadBatchAsync(
                    large, DownloadConcurrency, total, totalBytes, counters,
                    fileProgress, byteProgress, cancellationToken)
                .ConfigureAwait(false);

            byteProgress?.Report(new ByteProgress(totalBytes, Volatile.Read(ref counters.Bytes)));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task DownloadBatchAsync(
        List<GameFile> batch,
        int concurrency,
        int total,
        long totalBytes,
        ProgressCounters counters,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellationToken
            },
            async (file, ct) =>
            {
                await DownloadWithRetryAsync(file, ct).ConfigureAwait(false);
                await file.ExecuteUpdateTask(ct).ConfigureAwait(false);
                FinishOne(file, counters, total, totalBytes,
                    fileProgress, byteProgress, forceProgress: !IsSmallDownload(file));
            }).ConfigureAwait(false);
    }

    private sealed class ProgressCounters
    {
        public int Done;
        public long Bytes;
    }

    private static bool IsSmallDownload(GameFile file)
    {
        if (file.Size > 0 && file.Size <= SmallDownloadBytes)
            return true;
        if (file.Size > SmallDownloadBytes)
            return false;

        // Size unknown — treat common sound/texture names as small.
        var name = file.Name ?? file.Path ?? string.Empty;
        return name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static void FinishOne(
        GameFile file,
        ProgressCounters counters,
        int total,
        long totalBytes,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress,
        bool forceProgress)
    {
        var size = Math.Max(0L, file.Size);
        if (size > 0)
            Interlocked.Add(ref counters.Bytes, size);

        var done = Interlocked.Increment(ref counters.Done);
        // Small assets: throttle UI churn (coalescer helps, but fewer posts = less overhead).
        if (forceProgress || done == total || done % 16 == 0)
        {
            fileProgress?.Report(new InstallerProgressChangedEventArgs(
                total, done, file.Name, InstallerEventType.Done));
        }

        if (byteProgress is not null && (done % 32 == 0 || done == total))
            byteProgress.Report(new ByteProgress(totalBytes, Volatile.Read(ref counters.Bytes)));
    }

    private static List<GameFile> Deduplicate(IEnumerable<GameFile> gameFiles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<GameFile>();
        foreach (var file in gameFiles)
        {
            if (string.IsNullOrEmpty(file.Path) || string.IsNullOrEmpty(file.Url))
                continue;
            if (!seen.Add(file.Path!))
                continue;
            list.Add(file);
        }

        return list;
    }

    private static bool NeedsDownload(GameFile file)
    {
        try
        {
            var path = file.Path!;
            var tmp = path + TempSuffix;
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
                return true;
            }

            if (!File.Exists(path))
                return true;

            if (file.Size > 0 && new FileInfo(path).Length != file.Size)
                return true;

            // Full integrity check — size alone is not enough (corrupt same-length files).
            if (!string.IsNullOrWhiteSpace(file.Hash) && !Sha1Matches(path, file.Hash))
                return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool Sha1Matches(string path, string expectedHex)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.SequentialScan);
            var hash = SHA1.HashData(stream);
            var actual = Convert.ToHexString(hash);
            return actual.Equals(expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadWithRetryAsync(GameFile file, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadOnceAsync(file, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetries)
            {
                last = ex;
                var delay = attempt switch
                {
                    1 => 400,
                    2 => 900,
                    3 => 1800,
                    4 => 3200,
                    5 => 5000,
                    6 => 8000,
                    _ => 12000
                };
                // Extra wait when mirrors rate-limit (429).
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
                    delay = Math.Max(delay, 2500 * attempt);

                Debug.WriteLine($"[ArdelGameInstaller] retry {attempt} {file.Name}: {ex.Message}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new IOException(Loc.Format(LocKeys.Error_FileDownloadFailed, file.Name));
    }

    private async Task DownloadOnceAsync(GameFile file, CancellationToken cancellationToken)
    {
        var path = file.Path!;
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var tempPath = path + TempSuffix;
        try
        {
            using var response = await _http
                .GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
            {
                throw new HttpRequestException(
                    Loc.Format(LocKeys.Error_HttpStatus, (int)response.StatusCode),
                    null,
                    response.StatusCode);
            }

            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength ?? file.Size;

            if (length > 0 && length <= SmallFileBytes)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Hash in memory — avoid a second disk read for thousands of tiny .ogg files.
                if (!string.IsNullOrWhiteSpace(file.Hash) && !Sha1MatchesBytes(bytes, file.Hash))
                    throw new IOException(Loc.Format(LocKeys.Error_FileDownloadFailed, file.Name));

                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, path, overwrite: true);
                return;
            }

            await using (var dst = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferSize, FileOptions.SequentialScan))
            await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await src.CopyToAsync(dst, CopyBufferSize, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);

            if (!string.IsNullOrWhiteSpace(file.Hash) && !Sha1Matches(path, file.Hash))
            {
                try { File.Delete(path); } catch { /* ignore */ }
                throw new IOException(Loc.Format(LocKeys.Error_FileDownloadFailed, file.Name));
            }
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
            throw;
        }
    }

    private static bool Sha1MatchesBytes(ReadOnlySpan<byte> data, string expectedHex)
    {
        var hash = SHA1.HashData(data);
        var actual = Convert.ToHexString(hash);
        return actual.Equals(expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is HttpRequestException http)
            {
                if (http.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout
                    or null)
                    return true;
            }

            if (e is IOException or SocketException or TimeoutException)
                return true;
        }

        return false;
    }
}
