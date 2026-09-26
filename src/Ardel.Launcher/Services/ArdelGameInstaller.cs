using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Game-file installer tuned for ~5k Minecraft assets.
/// Avoids CmlLib <see cref="ParallelGameInstaller"/> Dataflow stalls and per-chunk progress.
/// </summary>
internal sealed class ArdelGameInstaller : IGameInstaller
{
    public const int DownloadConcurrency = ArdoInstallOptions.DefaultDownloadThreads;
    public const int SmallDownloadConcurrency = ArdoInstallOptions.DefaultSmallDownloadThreads;

    private const int SmallFileBytes = 512 * 1024;
    private const int SmallDownloadBytes = 256 * 1024;
    private const int DefaultCopyBufferSize = 64 * 1024;
    private const string TempSuffix = ".ardel-tmp";

    private readonly HttpClient _http;
    private readonly ArdoInstallOptions _options;
    private readonly SpeedLimiter? _limiter;
    private int _running;

    public ArdelGameInstaller(HttpClient httpClient, ArdoInstallOptions? options = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new ArdoInstallOptions();
        _limiter = _options.SpeedLimitKbps > 0
            ? new SpeedLimiter(_options.SpeedLimitKbps * 1024L)
            : null;
    }

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
            var checkConcurrency = Math.Clamp(_options.CheckConcurrency, 1, 256);
            var downloadThreads = Math.Clamp(_options.DownloadThreads, 1, 256);
            var smallThreads = Math.Clamp(_options.SmallDownloadThreads, downloadThreads, 256);

            fileProgress?.Report(new InstallerProgressChangedEventArgs(
                0, 0, Loc.Format(LocKeys.Download_CheckingFiles, 0, total), InstallerEventType.Done));

            var missing = new List<GameFile>(files.Count);
            var present = new List<GameFile>(files.Count);
            var gate = new object();
            var checkedCount = 0;

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = checkConcurrency,
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

            await Parallel.ForEachAsync(
                present,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = checkConcurrency,
                    CancellationToken = cancellationToken
                },
                async (file, ct) =>
                {
                    await file.ExecuteUpdateTask(ct).ConfigureAwait(false);
                    FinishOne(file, counters, total, totalBytes,
                        fileProgress, byteProgress, forceProgress: false);
                }).ConfigureAwait(false);

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
                    small, smallThreads, total, totalBytes, counters,
                    fileProgress, byteProgress, cancellationToken)
                .ConfigureAwait(false);

            await DownloadBatchAsync(
                    large, downloadThreads, total, totalBytes, counters,
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

    private bool NeedsDownload(GameFile file)
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

            if (_options.VerifySha1 &&
                !string.IsNullOrWhiteSpace(file.Hash) &&
                !Sha1Matches(path, file.Hash))
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
                DefaultCopyBufferSize, FileOptions.SequentialScan);
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
        var maxRetries = Math.Clamp(_options.MaxRetries, 1, 32);
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadOnceAsync(file, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
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
        var chunkSize = Math.Clamp(_options.ChunkSizeBytes, 256 * 1024, 64 * 1024 * 1024);
        try
        {
            // Multi-chunk for large files when size is known.
            if (file.Size > chunkSize * 2)
            {
                if (await TryDownloadChunkedAsync(file, tempPath, path, chunkSize, cancellationToken)
                        .ConfigureAwait(false))
                    return;
            }

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

                if (_limiter is not null)
                    await _limiter.ConsumeAsync(bytes.Length, cancellationToken).ConfigureAwait(false);

                if (_options.VerifySha1 &&
                    !string.IsNullOrWhiteSpace(file.Hash) &&
                    !Sha1MatchesBytes(bytes, file.Hash))
                    throw new IOException(Loc.Format(LocKeys.Error_FileDownloadFailed, file.Name));

                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, path, overwrite: true);
                return;
            }

            await using (var dst = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                DefaultCopyBufferSize, FileOptions.SequentialScan))
            await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await CopyThrottledAsync(src, dst, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);

            if (_options.VerifySha1 &&
                !string.IsNullOrWhiteSpace(file.Hash) &&
                !Sha1Matches(path, file.Hash))
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

    private async Task<bool> TryDownloadChunkedAsync(
        GameFile file,
        string tempPath,
        string path,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        var total = file.Size;
        if (total <= 0)
            return false;

        // Probe Accept-Ranges with a tiny ranged GET.
        using (var probe = new HttpRequestMessage(HttpMethod.Get, file.Url))
        {
            probe.Headers.Range = new RangeHeaderValue(0, Math.Min(chunkSize, total) - 1);
            using var probeResponse = await _http
                .SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
                return false;
        }

        await using (var dst = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
            DefaultCopyBufferSize, FileOptions.Asynchronous))
        {
            dst.SetLength(total);
        }

        var parts = (int)Math.Ceiling(total / (double)chunkSize);
        parts = Math.Clamp(parts, 2, Math.Min(8, _options.DownloadThreads));
        var partSize = (long)Math.Ceiling(total / (double)parts);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, parts),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parts,
                CancellationToken = cancellationToken
            },
            async (index, ct) =>
            {
                var start = index * partSize;
                if (start >= total)
                    return;
                var end = Math.Min(total, start + partSize) - 1;

                using var req = new HttpRequestMessage(HttpMethod.Get, file.Url);
                req.Headers.Range = new RangeHeaderValue(start, end);
                using var response = await _http
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(
                    tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite,
                    DefaultCopyBufferSize, FileOptions.Asynchronous);
                dst.Seek(start, SeekOrigin.Begin);
                await CopyThrottledAsync(src, dst, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        File.Move(tempPath, path, overwrite: true);

        if (_options.VerifySha1 &&
            !string.IsNullOrWhiteSpace(file.Hash) &&
            !Sha1Matches(path, file.Hash))
        {
            try { File.Delete(path); } catch { /* ignore */ }
            throw new IOException(Loc.Format(LocKeys.Error_FileDownloadFailed, file.Name));
        }

        return true;
    }

    private async Task CopyThrottledAsync(Stream src, Stream dst, CancellationToken cancellationToken)
    {
        var buffer = new byte[DefaultCopyBufferSize];
        while (true)
        {
            var read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
                break;

            if (_limiter is not null)
                await _limiter.ConsumeAsync(read, cancellationToken).ConfigureAwait(false);

            await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
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

    private sealed class SpeedLimiter
    {
        private readonly long _bytesPerSecond;
        private readonly object _gate = new();
        private long _windowStartTicks;
        private long _usedInWindow;

        public SpeedLimiter(long bytesPerSecond)
        {
            _bytesPerSecond = Math.Max(1024, bytesPerSecond);
            _windowStartTicks = Environment.TickCount64;
        }

        public async Task ConsumeAsync(int bytes, CancellationToken cancellationToken)
        {
            while (true)
            {
                long delayMs;
                lock (_gate)
                {
                    var now = Environment.TickCount64;
                    if (now - _windowStartTicks >= 1000)
                    {
                        _windowStartTicks = now;
                        _usedInWindow = 0;
                    }

                    if (_usedInWindow + bytes <= _bytesPerSecond)
                    {
                        _usedInWindow += bytes;
                        return;
                    }

                    delayMs = Math.Max(1, 1000 - (now - _windowStartTicks));
                }

                await Task.Delay((int)delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
