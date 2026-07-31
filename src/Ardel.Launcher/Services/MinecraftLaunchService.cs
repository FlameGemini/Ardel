using System.Diagnostics;
using System.Net;
using System.Net.Http;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionLoader;
using Optifine.Installer;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>
/// Thin wrapper around CmlLib.Core <see cref="MinecraftLauncher"/>.
/// Install/launch facade with optional BMCLAPI URL rewriting and parallel downloads.
/// </summary>
public sealed class MinecraftLaunchService : IMinecraftLaunchService
{
    public const string BmclApiBase = BmclApiMirrorHandler.BmclApiBase;

    /// <summary>
    /// HTTP pool sized above <see cref="ArdelGameInstaller.DownloadConcurrency"/>.
    /// </summary>
    private const int HttpMaxConnectionsPerServer = 64;
    /// <summary>Hard ceiling for loader-list HTTP. Cancel tokens alone are unreliable.</summary>
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(3);
    private static readonly System.Text.RegularExpressions.Regex VersionJsonRegex = new(
        "\"version\"\\s*:\\s*\"([^\"]+)\"",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly SettingsService _settingsService;
    private readonly object _httpLock = new();
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private HttpClient? _httpOfficial;
    private HttpClient? _httpBmcl;
    private HttpClient? _httpListOfficial;
    private HttpClient? _httpListBmcl;
    private IReadOnlyList<GameVersionItem>? _cachedVersions;
    private bool _cachedVersionsBmcl;

    /// <summary>
    /// Wired for the duration of <see cref="InstallCoreAsync"/> so nested
    /// <c>launcher.InstallAsync</c> calls bypass CmlLib's UI-bound <see cref="Progress{T}"/>.
    /// </summary>
    private IProgress<InstallerProgressChangedEventArgs>? _cmlFileProgress;
    private IProgress<ByteProgress>? _cmlByteProgress;

    public MinecraftLaunchService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public MinecraftLauncher CreateLauncher(LauncherSettings settings) =>
        CreateLauncherCore(settings, heavyInstaller: true);

    /// <summary>
    /// Lightweight launcher for listing Forge/NeoForge builds (no parallel installer fan-out).
    /// </summary>
    private MinecraftLauncher CreateListingLauncher(LauncherSettings settings) =>
        CreateLauncherCore(settings, heavyInstaller: false);

    private MinecraftLauncher CreateLauncherCore(LauncherSettings settings, bool heavyInstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var gameDir = string.IsNullOrWhiteSpace(settings.GameDirectory)
            ? GamePaths.GetMinecraftRoot()
            : settings.GameDirectory;

        Directory.CreateDirectory(gameDir);

        var path = new MinecraftPath(gameDir);
        var http = GetHttpClient(settings.UseBmclApi);
        var parameters = MinecraftLauncherParameters.CreateDefault(path, http);

        if (heavyInstaller)
            parameters.GameInstaller = GameInstallerFactory.Create(http);

        // Always skip Mojang's bundled JRE (~600+ files, last in the queue).
        // Ardel launches with system Java (Settings / JavaLocator); downloading the
        // runtime is what made ~4500/5000 feel like a stall.
        ConfigureFileExtractors(parameters, settings.UseBmclApi, skipBundledJava: true);

        return new MinecraftLauncher(parameters);
    }

    public static void ApplyBmclApiMirrors(MinecraftLauncherParameters parameters) =>
        ConfigureFileExtractors(parameters, useBmclApi: true, skipBundledJava: true);

    private static void ConfigureFileExtractors(
        MinecraftLauncherParameters parameters,
        bool useBmclApi,
        bool skipBundledJava)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        try
        {
            if (useBmclApi)
            {
                parameters.VersionLoader = new MojangJsonVersionLoaderV2(
                    parameters.MinecraftPath!,
                    parameters.HttpClient,
                    $"{BmclApiBase}/mc/game/version_manifest_v2.json");
            }

            var extractors = DefaultFileExtractors.CreateDefault(
                parameters.HttpClient,
                parameters.RulesEvaluator!,
                parameters.JavaPathResolver!);

            if (useBmclApi)
            {
                // Mojang libraries -> /libraries (not /maven). /maven is Forge/Fabric only.
                if (extractors.Asset is not null)
                    extractors.Asset.AssetServer = $"{BmclApiBase}/assets";

                if (extractors.Library is not null)
                    extractors.Library.LibraryServer = $"{BmclApiBase}/libraries";

                if (extractors.Java is not null)
                    extractors.Java.JavaManifestServer =
                        $"{BmclApiBase}/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
            }

            if (skipBundledJava)
                extractors.Java = null;

            parameters.FileExtractors = extractors.ToExtractorCollection();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] File extractor setup failed: {ex}");
            if (useBmclApi)
                throw new InvalidOperationException(Loc.Get(LocKeys.Error_BmclSetupFailed), ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<GameVersionItem>> GetVersionsAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_cachedVersions is not null && _cachedVersionsBmcl == settings.UseBmclApi)
            {
                OfficialJavaRequirements.ApplyCache(_cachedVersions);
                return MarkInstalled(_cachedVersions, settings);
            }

            // Disk cache: paint the Download page immediately on cold start.
            var disk = TryLoadVersionsFromDisk(settings.UseBmclApi);
            if (disk is { Count: > 0 })
            {
                OfficialJavaRequirements.ApplyCache(disk);
                _cachedVersions = disk;
                _cachedVersionsBmcl = settings.UseBmclApi;
                // Refresh in background; next visit / refresh picks up newer manifest.
                _ = RefreshVersionManifestAsync(settings);
                return MarkInstalled(disk, settings);
            }

            var items = await FetchVersionManifestAsync(settings, cancellationToken)
                .ConfigureAwait(false);
            _cachedVersions = items;
            _cachedVersionsBmcl = settings.UseBmclApi;
            return MarkInstalled(items, settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] GetVersions failed: {ex}");
            throw;
        }
    }

    public void InvalidateVersionCache()
    {
        _cachedVersions = null;
    }

    private async Task RefreshVersionManifestAsync(LauncherSettings settings)
    {
        try
        {
            var items = await FetchVersionManifestAsync(settings, CancellationToken.None)
                .ConfigureAwait(false);
            _cachedVersions = items;
            _cachedVersionsBmcl = settings.UseBmclApi;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Background manifest refresh failed: {ex.Message}");
        }
    }

    private async Task<List<GameVersionItem>> FetchVersionManifestAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        var http = GetListingHttpClient(settings.UseBmclApi);
        var url = settings.UseBmclApi
            ? $"{BmclApiBase}/mc/game/version_manifest_v2.json"
            : "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
        TrySaveRawManifestToDisk(settings.UseBmclApi, bytes);

        using var doc = System.Text.Json.JsonDocument.Parse(bytes);
        var items = ParseVersionManifest(doc.RootElement, cancellationToken);
        OfficialJavaRequirements.ApplyCache(items);
        return items;
    }

    private static List<GameVersionItem> ParseVersionManifest(
        System.Text.Json.JsonElement root,
        CancellationToken cancellationToken)
    {
        var items = new List<GameVersionItem>();
        if (!root.TryGetProperty("versions", out var versions) ||
            versions.ValueKind != System.Text.Json.JsonValueKind.Array)
            return items;

        foreach (var v in versions.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = v.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var type = v.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString() ?? "release"
                : "release";
            DateTimeOffset? releaseTime = null;
            if (v.TryGetProperty("releaseTime", out var rt) &&
                rt.ValueKind == System.Text.Json.JsonValueKind.String &&
                DateTimeOffset.TryParse(rt.GetString(), out var parsed))
                releaseTime = parsed;

            var metadataUrl = v.TryGetProperty("url", out var urlProp)
                ? urlProp.GetString()
                : null;

            items.Add(new GameVersionItem
            {
                Id = id,
                Type = type,
                Kind = VersionKindDetector.DetectFromId(id),
                IsInstalled = false,
                ReleaseTime = releaseTime,
                MetadataUrl = metadataUrl
            });
        }

        items.Sort((a, b) =>
            (b.ReleaseTime ?? DateTimeOffset.MinValue).CompareTo(
                a.ReleaseTime ?? DateTimeOffset.MinValue));
        return items;
    }

    private static string VersionManifestCachePath(bool useBmclApi) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            useBmclApi ? "version_manifest_bmcl.json" : "version_manifest_official.json");

    private static List<GameVersionItem>? TryLoadVersionsFromDisk(bool useBmclApi)
    {
        try
        {
            var path = VersionManifestCachePath(useBmclApi);
            if (!File.Exists(path))
                return null;

            // Stale after 24h: still usable for instant paint; background refresh updates.
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var items = ParseVersionManifest(doc.RootElement, CancellationToken.None);
            return items.Count > 0 ? items : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Disk manifest cache read failed: {ex.Message}");
            return null;
        }
    }

    private static string? TryFindMetadataUrl(string versionId, bool useBmclApi)
    {
        try
        {
            var path = VersionManifestCachePath(useBmclApi);
            if (!File.Exists(path))
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("versions", out var versions) ||
                versions.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            foreach (var v in versions.EnumerateArray())
            {
                if (!v.TryGetProperty("id", out var idProp) ||
                    !string.Equals(idProp.GetString(), versionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                return v.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Metadata URL lookup failed: {ex.Message}");
        }

        return null;
    }

    private static void TrySaveRawManifestToDisk(bool useBmclApi, byte[] bytes)
    {
        try
        {
            var path = VersionManifestCachePath(useBmclApi);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Disk manifest cache write failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<GameVersionItem> MarkInstalled(
        IReadOnlyList<GameVersionItem> items,
        LauncherSettings settings)
    {
        var versionsDir = Path.Combine(
            string.IsNullOrWhiteSpace(settings.GameDirectory)
                ? GamePaths.GetMinecraftRoot()
                : settings.GameDirectory,
            "versions");
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(versionsDir))
        {
            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                    continue;
                var jsonPath = Path.Combine(dir, name + ".json");
                var jarPath = Path.Combine(dir, name + ".jar");
                if (File.Exists(jsonPath) && File.Exists(jarPath))
                    installed.Add(name);
            }
        }

        foreach (var item in items)
            item.IsInstalled = installed.Contains(item.Id);

        return items;
    }

    public async Task InstallAsync(
        LauncherSettings settings,
        string versionId,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        await InstallCoreAsync(
                settings,
                async launcher =>
                {
                    await InstallVersionFilesAsync(launcher, versionId, cancellationToken)
                        .ConfigureAwait(false);
                    return versionId;
                },
                fileProgress,
                byteProgress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> InstallAsync(
        LauncherSettings settings,
        InstallRequest request,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MinecraftVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomVersionName);

        var nameError = NameRules.ValidateVersionName(request.CustomVersionName);
        if (nameError is not null)
            throw new InvalidOperationException(nameError);

        var customId = request.CustomVersionName.Trim();
        var mcRoot = settings.GameDirectory;
        if (GamePaths.IsVersionFullyInstalled(customId, mcRoot))
        {
            // portable: reclaim a hidden Forge/Fabric parent as a real vanilla instance.
            var reclaimDependency = request.Loader == ModLoaderKind.None &&
                                    GamePaths.IsDependencyOnly(customId, mcRoot);
            if (!reclaimDependency)
                throw new InvalidOperationException(Loc.Get(LocKeys.Validate_VersionExists));
        }

        var conflict = AddonConflictRules.ValidateInstall(request.Loader);
        if (conflict is not null)
            throw new InvalidOperationException(conflict);

        // Fabric/Forge profiles inheritFrom the vanilla id  - using the same folder name
        // overwrites vanilla JSON and creates a circular inheritsFrom (looks like "vanilla").
        if (request.Loader != ModLoaderKind.None &&
            string.Equals(
                customId,
                request.MinecraftVersionId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.Get(LocKeys.Validate_LoaderNameEqualsMc));
        }

        return await InstallCoreAsync(
                settings,
                async launcher =>
                {
                    var mcId = request.MinecraftVersionId.Trim();
                    var customId = request.CustomVersionName.Trim();
                    var http = GetHttpClient(settings.UseBmclApi);
                    var vanillaExisted = GamePaths.IsVersionFullyInstalled(mcId, settings.GameDirectory);

                    string installedId = request.Loader switch
                    {
                        ModLoaderKind.OptiFine => await InstallOptiFineProfileAsync(
                                launcher, http, settings, mcId, customId, request.LoaderVersion, cancellationToken)
                            .ConfigureAwait(false),
                        ModLoaderKind.Fabric => await InstallFabricAsync(
                                launcher, http, mcId, customId, request.LoaderVersion, cancellationToken)
                            .ConfigureAwait(false),
                        ModLoaderKind.Forge => await InstallForgeAsync(
                                launcher, http, settings, mcId, customId, request.LoaderVersion, cancellationToken)
                            .ConfigureAwait(false),
                        ModLoaderKind.NeoForge => await InstallNeoForgeAsync(
                                launcher, settings, mcId, customId, request.LoaderVersion, cancellationToken)
                            .ConfigureAwait(false),
                        _ => await InstallVanillaAsAsync(
                                launcher, mcId, customId, vanillaExisted, cancellationToken)
                            .ConfigureAwait(false)
                    };

                    await InstallVersionFilesAsync(launcher, installedId, cancellationToken)
                        .ConfigureAwait(false);

                    // CmlLib may re-fetch Mojang JSON when the folder name equals a release id
                    // (SHA1 mismatch -> overwrite). Guard loader profiles after file install.
                    if (request.Loader == ModLoaderKind.Fabric)
                        EnsureFabricProfileIntact(
                            launcher.MinecraftPath.Versions,
                            installedId);

                    GamePaths.EnsureVersionIsolation(installedId, settings.GameDirectory);

                    if (request.Loader == ModLoaderKind.None)
                    {
                        // Explicit vanilla (or renamed)  - always listed, even if named like 1.21.11.
                        GamePaths.MarkAsUserInstance(installedId, settings.GameDirectory);
                    }
                    else
                    {
                        // Parent vanilla stays on disk for inheritsFrom, but hidden until the user
                        // installs it themselves (portable dependency vs instance).
                        GamePaths.MarkAsDependency(mcId, settings.GameDirectory);
                        GamePaths.MarkAsUserInstance(installedId, settings.GameDirectory);
                    }

                    if (request.Loader == ModLoaderKind.Fabric && request.InstallFabricApi)
                    {
                        var modsDir = Path.Combine(
                            GamePaths.GetVersionInstanceDirectory(installedId, settings.GameDirectory),
                            "mods");
                        var fabricApi = new FabricApiModService(GetHttpClient(false));
                        var status = new DirectProgress<string>(message =>
                            fileProgress?.Report(new FileProgressInfo(message, 0, 0)));
                        await fabricApi
                            .InstallAsync(mcId, modsDir, status, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return installedId;
                },
                fileProgress,
                byteProgress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> InstallCoreAsync(
        LauncherSettings settings,
        Func<MinecraftLauncher, Task<string>> work,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            // One install at a time  - without status updates the UI stays on "Waiting - 
            // while launch verify or another download holds the gate.
            if (!await _installGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                fileProgress?.Report(new FileProgressInfo(
                    Loc.Get(LocKeys.Download_WaitingGate), 0, 0));
                await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            acquired = true;
            fileProgress?.Report(new FileProgressInfo(
                Loc.Get(LocKeys.Download_InitLauncher), 0, 0));

            var launcher = CreateLauncher(settings);

            // CRITICAL: do NOT use launcher.FileProgressChanged / Progress<T>.
            // MinecraftLauncher builds Progress<T> with the UI SynchronizationContext; each of
            // ~5k file events is posted to the UI thread and eventually starves downloads (~1/s).
            _cmlFileProgress = fileProgress is null
                ? null
                : new DirectProgress<InstallerProgressChangedEventArgs>(e =>
                {
                    if (e.EventType == InstallerEventType.Queued)
                        return;
                    fileProgress.Report(new FileProgressInfo(
                        e.Name ?? string.Empty,
                        e.ProgressedTasks,
                        e.TotalTasks));
                });

            _cmlByteProgress = byteProgress is null
                ? null
                : new DirectProgress<ByteProgress>(e =>
                    byteProgress.Report(new ByteProgressInfo(e.ProgressedBytes, e.TotalBytes)));

            fileProgress?.Report(new FileProgressInfo(
                Loc.Get(LocKeys.Download_ResolvingFiles), 0, 0));

            try
            {
                return await work(launcher).ConfigureAwait(false);
            }
            finally
            {
                _cmlFileProgress = null;
                _cmlByteProgress = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Install failed: {ex}");
            throw;
        }
        finally
        {
            if (acquired)
                _installGate.Release();
        }
    }

    /// <summary>
    /// Install game files with side-channel progress (never the UI-bound default Progress).
    /// </summary>
    private async Task InstallVersionFilesAsync(
        MinecraftLauncher launcher,
        string versionId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var innerFile = _cmlFileProgress;
        IProgress<InstallerProgressChangedEventArgs>? progress = innerFile is null
            ? null
            : new DirectProgress<InstallerProgressChangedEventArgs>(e =>
            {
                // Real file progress means metadata resolve is done  - stop the heartbeat.
                if (e.TotalTasks > 0)
                    heartbeatCts.Cancel();
                innerFile.Report(e);
            });

        progress?.Report(new InstallerProgressChangedEventArgs(
            0, 0, Loc.Get(LocKeys.Download_ResolvingFiles), InstallerEventType.Done));

        // CmlLib can sit on version_manifest / asset index for a long time with no callbacks.
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(2000, heartbeatCts.Token).ConfigureAwait(false);
                    var secs = Math.Max(1, (int)started.Elapsed.TotalSeconds);
                    progress?.Report(new InstallerProgressChangedEventArgs(
                        0, 0,
                        Loc.Format(LocKeys.Download_ResolvingElapsed, secs),
                        InstallerEventType.Done));
                }
            }
            catch (OperationCanceledException)
            {
                // expected when file progress starts or install finishes
            }
        }, CancellationToken.None);

        try
        {
            await launcher
                .InstallAsync(versionId, progress, _cmlByteProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat.ConfigureAwait(false); } catch { /* ignore */ }
        }

        GamePaths.MarkLaunchReady(versionId, launcher.MinecraftPath.BasePath);
    }

    private async Task<string> InstallVanillaAsAsync(
        MinecraftLauncher launcher,
        string mcId,
        string customId,
        bool vanillaExisted,
        CancellationToken cancellationToken)
    {
        await InstallVersionFilesAsync(launcher, mcId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(mcId, customId, StringComparison.OrdinalIgnoreCase))
            return customId;

        // Keep a pre-existing vanilla install; otherwise rename the fresh download.
        GamePaths.PublishVersionAs(mcId, customId, copy: vanillaExisted);
        return customId;
    }

    public async Task<IReadOnlyList<ModLoaderVersionOption>> GetModLoaderVersionsAsync(
        LauncherSettings settings,
        string minecraftVersionId,
        ModLoaderKind loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersionId);
        if (loader == ModLoaderKind.None)
            return [];

        var mcId = minecraftVersionId.Trim();
        try
        {
            return loader switch
            {
                ModLoaderKind.Fabric => await ListFabricAsync(settings, mcId, cancellationToken)
                    .ConfigureAwait(false),
                ModLoaderKind.Forge => await ListForgeAsync(settings, mcId, cancellationToken)
                    .ConfigureAwait(false),
                ModLoaderKind.NeoForge => await ListNeoForgeAsync(settings, mcId, cancellationToken)
                    .ConfigureAwait(false),
                ModLoaderKind.OptiFine => await GetOptiFineVersionsAsync(
                        settings, mcId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false),
                _ => []
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] GetModLoaderVersions failed: {ex}");
            throw;
        }
    }

    public async Task<IReadOnlyList<ModLoaderVersionOption>> GetOptiFineVersionsAsync(
        LauncherSettings settings,
        string minecraftVersionId,
        string? forgeVersionFilter = null,
        CancellationToken cancellationToken = default)
    {
        _ = forgeVersionFilter;
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersionId);
        var mcId = minecraftVersionId.Trim();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await ListOptiFineFromBmclAsync(mcId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] GetOptiFineVersions failed: {ex}");
            throw;
        }
    }

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListOptiFineFromBmclAsync(
        string mcId,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ListTimeout);

        var http = GetListingHttpClient(mirror: true);
        var url = $"{BmclApiBase}/optifine/{Uri.EscapeDataString(mcId)}";
        using var response = await http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token)
            .ConfigureAwait(false);
        var entries = await System.Text.Json.JsonSerializer
            .DeserializeAsync<List<BmclOptiFineEntry>>(stream, cancellationToken: timeoutCts.Token)
            .ConfigureAwait(false);

        if (entries is null || entries.Count == 0)
            return [];

        return entries
            .Select(e =>
            {
                var type = string.IsNullOrWhiteSpace(e.Type) ? "HD_U" : e.Type.Trim();
                var patch = (e.Patch ?? string.Empty).Trim();
                var edition = string.IsNullOrEmpty(patch) ? type : $"{type}_{patch}";
                var fileName = e.Filename ?? string.Empty;
                var isPreview = fileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase)
                    || patch.Contains("pre", StringComparison.OrdinalIgnoreCase);
                var id = isPreview
                    ? $"preview_OptiFine_{mcId}_{edition}"
                    : $"OptiFine_{mcId}_{edition}";
                return new ModLoaderVersionOption
                {
                    Id = id,
                    DisplayName = isPreview
                        ? Loc.Format(LocKeys.LoaderTag_Named, id, Loc.Get(LocKeys.LoaderTag_Unstable))
                        : id,
                    IsPreview = isPreview,
                    IsStable = !isPreview
                };
            })
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(v => v.IsPreview)
            .ThenByDescending(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModLoaderVersionOption MapOptiFineOption(OptifineVersion v)
    {
        var display = v.IsPreviewVersion
            ? Loc.Format(LocKeys.LoaderTag_Named, v.Version, Loc.Get(LocKeys.LoaderTag_Unstable))
            : v.Version;
        return new ModLoaderVersionOption
        {
            Id = v.Version,
            DisplayName = display,
            IsPreview = v.IsPreviewVersion,
            IsStable = !v.IsPreviewVersion
        };
    }

    private async Task<string> InstallOptiFineProfileAsync(
        MinecraftLauncher launcher,
        HttpClient http,
        LauncherSettings settings,
        string mcId,
        string customId,
        string? optiFineVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optiFineVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await InstallVersionFilesAsync(launcher, mcId, cancellationToken).ConfigureAwait(false);

        string installed;
        if (settings.UseBmclApi)
        {
            installed = await InstallOptiFineFromBmclAsync(
                    http,
                    launcher.MinecraftPath.BasePath,
                    mcId,
                    optiFineVersion.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var installer = new OptifineInstaller(http);
            installed = await installer
                .InstallOptifineAsync(launcher.MinecraftPath.BasePath, optiFineVersion.Trim())
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(installed))
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_LoaderEmptyId));

        if (!string.Equals(installed, customId, StringComparison.OrdinalIgnoreCase))
            GamePaths.PublishVersionAs(installed, customId, copy: false, settings.GameDirectory);

        return customId;
    }

    /// <summary>
    /// Download OptiFine jar from BMCLAPI and run the same local patch as Optifine.Installer.
    /// </summary>
    private static async Task<string> InstallOptiFineFromBmclAsync(
        HttpClient http,
        string minecraftPath,
        string mcId,
        string optiFineVersionId,
        CancellationToken cancellationToken)
    {
        var listUrl = $"{BmclApiBase}/optifine/{Uri.EscapeDataString(mcId)}";
        using var listResponse = await http.GetAsync(listUrl, cancellationToken).ConfigureAwait(false);
        listResponse.EnsureSuccessStatusCode();
        await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = await System.Text.Json.JsonSerializer
            .DeserializeAsync<List<BmclOptiFineEntry>>(listStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? [];

        var match = entries.FirstOrDefault(e =>
        {
            var type = string.IsNullOrWhiteSpace(e.Type) ? "HD_U" : e.Type.Trim();
            var patch = (e.Patch ?? string.Empty).Trim();
            var edition = string.IsNullOrEmpty(patch) ? type : $"{type}_{patch}";
            var fileName = e.Filename ?? string.Empty;
            var isPreview = fileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase)
                || patch.Contains("pre", StringComparison.OrdinalIgnoreCase);
            var id = isPreview
                ? $"preview_OptiFine_{mcId}_{edition}"
                : $"OptiFine_{mcId}_{edition}";
            return string.Equals(id, optiFineVersionId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, optiFineVersionId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    Path.GetFileNameWithoutExtension(fileName),
                    optiFineVersionId,
                    StringComparison.OrdinalIgnoreCase);
        });

        if (match is null)
            throw new InvalidOperationException(Loc.Get(LocKeys.Install_NoOptiFine));

        var typeName = string.IsNullOrWhiteSpace(match.Type) ? "HD_U" : match.Type.Trim();
        var patchName = (match.Patch ?? string.Empty).Trim();
        var edition = string.IsNullOrEmpty(patchName) ? typeName : $"{typeName}_{patchName}";
        var fileName = string.IsNullOrWhiteSpace(match.Filename)
            ? $"OptiFine_{mcId}_{edition}.jar"
            : match.Filename.Trim();
        var isPreview = fileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase)
            || patchName.Contains("pre", StringComparison.OrdinalIgnoreCase);

        var jarUrl =
            $"{BmclApiBase}/optifine/{Uri.EscapeDataString(mcId)}/{Uri.EscapeDataString(typeName)}/{Uri.EscapeDataString(patchName)}";
        var tempJar = Path.Combine(Path.GetTempPath(), fileName);

        using (var jarResponse = await http.GetAsync(jarUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                   .ConfigureAwait(false))
        {
            jarResponse.EnsureSuccessStatusCode();
            await using var input = await jarResponse.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                tempJar, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var forge = match.Forge?
            .Replace("Forge ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("#", "", StringComparison.Ordinal)
            .Trim();
        if (string.Equals(forge, "N/A", StringComparison.OrdinalIgnoreCase))
            forge = null;

        var ofVersion = new OptifineVersion(mcId, edition, forge, isPreview, DateTime.UtcNow);
        InvokeOptiFineDoInstall(minecraftPath, tempJar, ofVersion);
        return $"{mcId}-OptiFine_{edition}";
    }

    private static void InvokeOptiFineDoInstall(
        string minecraftPath,
        string jarPath,
        OptifineVersion version)
    {
        var method = typeof(OptifineInstaller).GetMethod(
            "DoInstall",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null)
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_LoaderEmptyId));

        try
        {
            method.Invoke(
                null,
                [new DirectoryInfo(minecraftPath), new FileInfo(jarPath), version]);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
        finally
        {
            if (File.Exists(jarPath))
            {
                try { File.Delete(jarPath); } catch { /* ignore */ }
            }
        }
    }

    private sealed class BmclOptiFineEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("mcversion")]
        public string? McVersion { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string? Type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("patch")]
        public string? Patch { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("forge")]
        public string? Forge { get; set; }
    }

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListFabricAsync(
        LauncherSettings settings,
        string mcId,
        CancellationToken cancellationToken)
    {
        // Fabric meta list via BMCL rewrite on listing client (fast / reliable).
        _ = settings;
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ListTimeout);
        var fabric = new FabricInstaller(GetListingHttpClient(mirror: true));
        return MapFabricLoaders(
            await fabric.GetLoaders(mcId).WaitAsync(timeoutCts.Token).ConfigureAwait(false));
    }

    private static List<ModLoaderVersionOption> MapFabricLoaders(
        IEnumerable<FabricLoader> loaders) =>
        loaders
            .Where(l => !string.IsNullOrWhiteSpace(l.Version))
            .Select(l =>
            {
                var id = l.Version!;
                var tag = l.Stable
                    ? Loc.Get(LocKeys.LoaderTag_Stable)
                    : Loc.Get(LocKeys.LoaderTag_Unstable);
                return new ModLoaderVersionOption
                {
                    Id = id,
                    DisplayName = Loc.Format(LocKeys.LoaderTag_Named, id, tag),
                    IsStable = l.Stable,
                    IsLatest = false,
                    IsRecommended = l.Stable
                };
            })
            .ToList();

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListForgeAsync(
        LauncherSettings settings,
        string mcId,
        CancellationToken cancellationToken)
    {
        // List from BMCL forge index (fast). Install jars still follow UseBmclApi.
        _ = settings;
        cancellationToken.ThrowIfCancellationRequested();
        return await ListForgeFromBmclAsync(mcId, cancellationToken).ConfigureAwait(false);
    }

    private static List<ModLoaderVersionOption> MapForgeVersions(IEnumerable<ForgeVersion> versions) =>
        versions
            .Where(v => !string.IsNullOrWhiteSpace(v.ForgeVersionName))
            .Select(v =>
            {
                var id = v.ForgeVersionName;
                var tags = new List<string>(2);
                if (v.IsRecommendedVersion)
                    tags.Add(Loc.Get(LocKeys.LoaderTag_Recommended));
                if (v.IsLatestVersion)
                    tags.Add(Loc.Get(LocKeys.LoaderTag_Latest));
                var display = tags.Count > 0
                    ? Loc.Format(LocKeys.LoaderTag_Named, id, string.Join(", ", tags))
                    : id;
                return new ModLoaderVersionOption
                {
                    Id = id,
                    DisplayName = display,
                    IsRecommended = v.IsRecommendedVersion,
                    IsLatest = v.IsLatestVersion
                };
            })
            .ToList();

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListForgeFromBmclAsync(
        string mcId,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ListTimeout);
        var http = GetListingHttpClient(mirror: true);
        var url = $"{BmclApiBase}/forge/minecraft/{Uri.EscapeDataString(mcId)}";
        using var response = await http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        var versions = new List<ModLoaderVersionOption>();
        foreach (System.Text.RegularExpressions.Match match in VersionJsonRegex.Matches(json))
        {
            var id = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            versions.Add(new ModLoaderVersionOption { Id = id, DisplayName = id });
        }

        versions.Reverse();
        if (versions.Count > 0)
        {
            var latest = versions[0];
            versions[0] = new ModLoaderVersionOption
            {
                Id = latest.Id,
                DisplayName = Loc.Format(
                    LocKeys.LoaderTag_Named,
                    latest.Id,
                    Loc.Get(LocKeys.LoaderTag_Latest)),
                IsLatest = true
            };
        }

        return versions;
    }

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListNeoForgeAsync(
        LauncherSettings settings,
        string mcId,
        CancellationToken cancellationToken)
    {
        _ = settings;
        cancellationToken.ThrowIfCancellationRequested();
        return await ListNeoForgeFromBmclAsync(mcId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ModLoaderVersionOption>> ListNeoForgeFromBmclAsync(
        string mcId,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ListTimeout);
        var http = GetListingHttpClient(mirror: true);
        var url = $"{BmclApiBase}/neoforge/list/{Uri.EscapeDataString(mcId)}";
        using var response = await http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        var versions = new List<ModLoaderVersionOption>();
        foreach (System.Text.RegularExpressions.Match match in VersionJsonRegex.Matches(json))
        {
            var id = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            versions.Add(new ModLoaderVersionOption { Id = id, DisplayName = id });
        }

        versions.Reverse();
        return versions;
    }

    private async Task<string> InstallFabricAsync(
        MinecraftLauncher launcher,
        HttpClient http,
        string mcId,
        string customId,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Parent vanilla must exist  - fabric profile inheritsFrom {mcId}.
        await InstallVersionFilesAsync(launcher, mcId, cancellationToken).ConfigureAwait(false);

        var fabric = new FabricInstaller(http);
        string installed;
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            installed = await fabric.Install(mcId, launcher.MinecraftPath, customId).ConfigureAwait(false);
        }
        else
        {
            installed = await fabric
                .Install(mcId, loaderVersion.Trim(), launcher.MinecraftPath, customId)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(installed))
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_LoaderEmptyId));

        // Fabric meta JSON keeps id=fabric-loader- -  align with the folder name we use.
        RewriteVersionJsonId(launcher.MinecraftPath.Versions, installed);
        EnsureFabricProfileIntact(launcher.MinecraftPath.Versions, installed);

        // Force version list refresh so the new profile is LocalVersionMetadata (IsSaved),
        // not looked up as a Mojang id that would re-download and wipe the JSON.
        await launcher.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);

        return installed;
    }

    private static void EnsureFabricProfileIntact(string versionsRoot, string versionName)
    {
        var jsonPath = Path.Combine(versionsRoot, versionName, versionName + ".json");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_FabricProfileInvalid));

        var json = File.ReadAllText(jsonPath);
        if (VersionKindDetector.DetectFromJson(json) != VersionKind.Fabric)
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_FabricProfileInvalid));
    }

    /// <summary>
    /// FabricInstaller copies meta JSON as-is (id still fabric-loader- - . Rewrite to folder name.
    /// </summary>
    private static void RewriteVersionJsonId(string versionsRoot, string versionName)
    {
        var jsonPath = Path.Combine(versionsRoot, versionName, versionName + ".json");
        if (!File.Exists(jsonPath))
            return;

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(jsonPath)) as System.Text.Json.Nodes.JsonObject;
            if (node is null)
                return;

            node["id"] = versionName;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(jsonPath, node.ToJsonString(opts));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] RewriteVersionJsonId failed: {ex.Message}");
        }
    }

    private static async Task<string> InstallForgeAsync(
        MinecraftLauncher launcher,
        HttpClient http,
        LauncherSettings settings,
        string mcId,
        string customId,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        var forge = new ForgeInstaller(launcher, http);
        var options = new ForgeInstallOptions
        {
            CancellationToken = cancellationToken,
            SkipIfAlreadyInstalled = false,
            JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath) ? null : settings.JavaPath
        };

        var installed = string.IsNullOrWhiteSpace(loaderVersion)
            ? await forge.Install(mcId, options).ConfigureAwait(false)
            : await forge.Install(mcId, loaderVersion.Trim(), options).ConfigureAwait(false);

        return await FinalizeLoaderVersionAsync(installed, customId, settings.GameDirectory)
            .ConfigureAwait(false);
    }

    private static async Task<string> InstallNeoForgeAsync(
        MinecraftLauncher launcher,
        LauncherSettings settings,
        string mcId,
        string customId,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        var neo = new NeoForgeInstaller(launcher);
        var options = new NeoForgeInstallOptions
        {
            CancellationToken = cancellationToken,
            SkipIfAlreadyInstalled = false,
            JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath) ? null : settings.JavaPath
        };

        var installed = string.IsNullOrWhiteSpace(loaderVersion)
            ? await neo.Install(mcId, options).ConfigureAwait(false)
            : await neo.Install(mcId, loaderVersion.Trim(), options).ConfigureAwait(false);

        return await FinalizeLoaderVersionAsync(installed, customId, settings.GameDirectory)
            .ConfigureAwait(false);
    }

    private static Task<string> FinalizeLoaderVersionAsync(
        string installedId,
        string customId,
        string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(installedId))
            throw new InvalidOperationException(Loc.Get(LocKeys.Error_LoaderEmptyId));

        if (!string.Equals(installedId, customId, StringComparison.OrdinalIgnoreCase))
        {
            // Loader folders are new  - rename (do not leave the default forge-* name behind).
            GamePaths.PublishVersionAs(installedId, customId, copy: false, gameDirectory);
            return Task.FromResult(customId);
        }

        return Task.FromResult(installedId);
    }

    public async Task<Process> LaunchAsync(
        LauncherSettings settings,
        string versionId,
        string playerName,
        IProgress<FileProgressInfo>? fileProgress,
        IProgress<ByteProgressInfo>? byteProgress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        void ReportStatus(string status) =>
            fileProgress?.Report(new FileProgressInfo(status, 0, 0));

        ReportStatus(Loc.Get(LocKeys.Home_ResolvingJava));
        var metadataUrl = _cachedVersions?
            .FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?.MetadataUrl;
        metadataUrl ??= TryFindMetadataUrl(versionId, settings.UseBmclApi);
        var required = await OfficialJavaRequirements
            .ResolveAsync(
                versionId,
                metadataUrl,
                settings.GameDirectory,
                GetListingHttpClient(settings.UseBmclApi),
                cancellationToken)
            .ConfigureAwait(false);
        string? javaPath = settings.JavaPath;

        if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
        {
            var actual = JavaLocator.GetJavaVersion(javaPath);
            if (actual < required)
                javaPath = null;
        }
        else
        {
            javaPath = null;
        }

        javaPath ??= JavaRuntimeInstaller.TryFindInstalled(required);
        javaPath ??= JavaLocator.FindBestMatch(required)?.JavaExePath;

        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            ReportStatus(Loc.Format(LocKeys.Home_DownloadingJava, required));
            javaPath = await JavaRuntimeInstaller
                .EnsureAsync(required, GetHttpClient(settings.UseBmclApi), byteProgress, cancellationToken)
                .ConfigureAwait(false);
        }

        javaPath = JavaRuntimeInstaller.PreferJavaw(javaPath);
        settings.JavaPath = javaPath;

        try
        {
            var gameDir = string.IsNullOrWhiteSpace(settings.GameDirectory)
                ? GamePaths.GetMinecraftRoot()
                : settings.GameDirectory;

            MinecraftLauncher launcher;
            if (GamePaths.IsLaunchReady(versionId, gameDir))
            {
                // Fast path: files already installed and marked ready — skip multi-second SHA1.
                ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
                launcher = CreateLauncherCore(settings, heavyInstaller: false);
            }
            else
            {
                ReportStatus(Loc.Format(LocKeys.Home_Preparing, versionId));
                launcher = await EnsureInstalledForLaunchAsync(
                        settings,
                        versionId,
                        fileProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
                ReportStatus(Loc.Format(LocKeys.Home_Starting, versionId));
            }

            var sessionName = string.IsNullOrWhiteSpace(playerName)
                ? Localization.Loc.Get(Localization.LocKeys.Default_PlayerName)
                : playerName.Trim();
            var session = MSession.CreateOfflineSession(sessionName);

            var root = launcher.MinecraftPath;
            var instanceDir = GamePaths.EnsureVersionIsolation(versionId, settings.GameDirectory);
            var launchPath = new MinecraftPath(instanceDir)
            {
                Library = root.Library,
                Assets = root.Assets,
                Versions = root.Versions,
                Runtime = root.Runtime,
                Resource = root.Resource
            };

            var option = new MLaunchOption
            {
                Session = session,
                MaximumRamMb = Math.Clamp(settings.MaxRamMb, 512, 65536),
                JavaPath = javaPath,
                Path = launchPath
            };

            // Reuse extracted natives when present to avoid Clean+Unzip on every launch.
            var nativesDir = Path.Combine(root.Versions, versionId, "natives");
            if (Directory.Exists(nativesDir) &&
                Directory.EnumerateFileSystemEntries(nativesDir).Any())
            {
                option.NativesDirectory = nativesDir;
            }

            var process = await launcher.BuildProcessAsync(versionId, option, cancellationToken)
                .ConfigureAwait(false);

            // Hide the console that appears when launching via java.exe.
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            if (!string.IsNullOrWhiteSpace(process.StartInfo.FileName))
                process.StartInfo.FileName = JavaRuntimeInstaller.PreferJavaw(process.StartInfo.FileName);

            ReportStatus(Loc.Get(LocKeys.Home_LaunchingGame));
            if (!process.Start())
                throw new InvalidOperationException(Loc.Get(LocKeys.Error_ProcessStartFailed));

            return process;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MinecraftLaunchService] Launch failed: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Install/repair game files for launch using one launcher instance (avoids a second manifest fetch).
    /// </summary>
    private async Task<MinecraftLauncher> EnsureInstalledForLaunchAsync(
        LauncherSettings settings,
        string versionId,
        IProgress<FileProgressInfo>? fileProgress,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            if (!await _installGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                fileProgress?.Report(new FileProgressInfo(
                    Loc.Get(LocKeys.Download_WaitingGate), 0, 0));
                await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            acquired = true;

            var gameDir = string.IsNullOrWhiteSpace(settings.GameDirectory)
                ? GamePaths.GetMinecraftRoot()
                : settings.GameDirectory;

            // Another download may have finished while we waited.
            if (GamePaths.IsLaunchReady(versionId, gameDir))
                return CreateLauncherCore(settings, heavyInstaller: false);

            fileProgress?.Report(new FileProgressInfo(
                Loc.Get(LocKeys.Download_InitLauncher), 0, 0));

            var launcher = CreateLauncher(settings);

            _cmlFileProgress = fileProgress is null
                ? null
                : new DirectProgress<InstallerProgressChangedEventArgs>(e =>
                {
                    if (e.EventType == InstallerEventType.Queued)
                        return;
                    fileProgress.Report(new FileProgressInfo(
                        e.Name ?? string.Empty,
                        e.ProgressedTasks,
                        e.TotalTasks));
                });
            _cmlByteProgress = null;

            try
            {
                fileProgress?.Report(new FileProgressInfo(
                    Loc.Get(LocKeys.Download_ResolvingFiles), 0, 0));
                await InstallVersionFilesAsync(launcher, versionId, cancellationToken)
                    .ConfigureAwait(false);
                return launcher;
            }
            finally
            {
                _cmlFileProgress = null;
                _cmlByteProgress = null;
            }
        }
        finally
        {
            if (acquired)
                _installGate.Release();
        }
    }

    private HttpClient GetHttpClient(bool useBmclApi)
    {
        lock (_httpLock)
        {
            if (useBmclApi)
                return _httpBmcl ??= CreateOptimizedHttpClient(mirror: true, listClient: false);

            return _httpOfficial ??= CreateOptimizedHttpClient(mirror: false, listClient: false);
        }
    }

    /// <summary>
    /// Short-timeout HTTP/1.1 client for loader version lists.
    /// Isolated from download clients. <paramref name="mirror"/> enables BMCL URL rewrite.
    /// </summary>
    private HttpClient GetListingHttpClient(bool mirror)
    {
        lock (_httpLock)
        {
            if (mirror)
                return _httpListBmcl ??= CreateOptimizedHttpClient(mirror: true, listClient: true);

            return _httpListOfficial ??= CreateOptimizedHttpClient(mirror: false, listClient: true);
        }
    }

    private static HttpClient CreateOptimizedHttpClient(bool mirror, bool listClient)
    {
        // Default socket handling (no custom ConnectCallback) so SocketsHttpHandler can
        // actually pool/reuse TCP+HTTP/2 streams  - custom connect was burning ephemeral ports
        // after ~4k requests and throughput collapsed to ~1 file/s.
        var sockets = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = listClient ? 8 : HttpMaxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = !listClient,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = listClient ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30)
        };

        HttpMessageHandler pipeline = sockets;
        // Always unwrap adfoc.us -> real maven URL before any download.
        pipeline = new AdfocInterceptHandler(pipeline);
        if (mirror)
            pipeline = new BmclApiMirrorHandler(pipeline);

        var client = new HttpClient(pipeline)
        {
            // List calls must fail fast  - CT cancel alone often waits until Timeout.
            Timeout = listClient ? TimeSpan.FromSeconds(3) : TimeSpan.FromMinutes(10),
            DefaultRequestVersion = listClient ? HttpVersion.Version11 : HttpVersion.Version20,
            DefaultVersionPolicy = listClient
                ? HttpVersionPolicy.RequestVersionOrLower
                : HttpVersionPolicy.RequestVersionOrLower
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ardel/1.0");
        client.DefaultRequestHeaders.ConnectionClose = false;
        return client;
    }
}

