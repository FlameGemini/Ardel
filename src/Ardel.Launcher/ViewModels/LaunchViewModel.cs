using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.Services.Auth;
using Ardel.Launcher.Services.CrashAnalysis;

namespace Ardel.Launcher.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly Lazy<IMinecraftLaunchService> _launchService;
    private readonly LocalVersionStore _localVersions;
    private readonly SettingsService _settingsService;
    private readonly InstanceSettingsStore _instanceSettings;
    private readonly AccountStore _accounts;
    private readonly SkinLibraryStore _skins;
    private readonly InstanceStatsStore _statsStore;
    private readonly MicrosoftAuthService _microsoftAuth;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _persistTimer;
    private LauncherSettings _settings = new();
    private readonly object _launchCtsGate = new();
    private readonly List<CancellationTokenSource> _launchCtsList = [];
    private int _launchingCount;
    private Process? _gameProcess;
    private readonly object _processGate = new();
    private readonly List<Process> _gameProcesses = [];
    private readonly Dictionary<int, TrackedGameSession> _processSessions = new();
    private readonly HashSet<int> _forceKilledProcessIds = new();
    private bool _minimizeUntilExitActive;
    private Views.GameLogWindow? _gameLogWindow;
    private DispatcherQueueTimer? _gameWatchTimer;
    private bool _gameWatchTimerRunning;
    private bool _suppressPersist = true;
    private bool _settingsLoaded;
    [ObservableProperty] private bool _isLocalReady;
    [ObservableProperty] private int _runningGamesCount;
    private int _javaProbeGeneration;
    private FileSystemWatcher? _versionsWatcher;
    private string? _versionsWatcherPath;
    private DispatcherQueueTimer? _versionsReloadTimer;
    private int _versionsReloadBusy;
    // Pre-warmed IsLaunchReady results keyed by versionId (invalidated on reload).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _launchReadyCache
        = new(StringComparer.OrdinalIgnoreCase);

    public LaunchViewModel(
        Lazy<IMinecraftLaunchService> launchService,
        LocalVersionStore localVersions,
        SettingsService settingsService,
        InstanceSettingsStore instanceSettings,
        AccountStore accounts,
        SkinLibraryStore skins,
        InstanceStatsStore statsStore,
        MicrosoftAuthService microsoftAuth,
        DispatcherQueue dispatcher)
    {
        _launchService = launchService;
        _localVersions = localVersions;
        _settingsService = settingsService;
        _instanceSettings = instanceSettings;
        _accounts = accounts;
        _skins = skins;
        _statsStore = statsStore;
        _microsoftAuth = microsoftAuth;
        _dispatcher = dispatcher;
        _accounts.Changed += (_, _) =>
            _dispatcher.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(HasSignedInAccount));
                LaunchGameCommand.NotifyCanExecuteChanged();
            });
        _persistTimer = dispatcher.CreateTimer();
        _persistTimer.IsRepeating = false;
        _persistTimer.Interval = TimeSpan.FromMilliseconds(400);
        _persistTimer.Tick += (_, _) => PersistNow();

        _versionsReloadTimer = dispatcher.CreateTimer();
        _versionsReloadTimer.IsRepeating = false;
        _versionsReloadTimer.Interval = TimeSpan.FromMilliseconds(750);
        _versionsReloadTimer.Tick += (_, _) => _ = ReloadLocalVersionsFromWatcherAsync();
        // No disk I/O / CmlLib in ctor — first paint stays light
    }

    public ObservableCollection<GameVersionItem> Versions { get; } = [];
    public ObservableCollection<JavaInstallation> JavaInstallations { get; } = [];

    /// <summary>True when an offline or Microsoft account is signed in and ready to launch.</summary>
    public bool HasSignedInAccount
    {
        get
        {
            var active = _accounts.GetActive();
            if (active is null)
                return false;

            if (active.Kind == AccountKind.Microsoft)
                return !string.IsNullOrWhiteSpace(active.MicrosoftAccountId);

            return active.Kind == AccountKind.Offline &&
                   _accounts.HasLicensedAccount &&
                   NameRules.ValidatePlayerName(active.DisplayName) is null;
        }
    }

    /// <summary>Raised on the UI thread when every tracked game process has exited.</summary>
    public event EventHandler? GameExited;

    [ObservableProperty] private GameVersionItem? _selectedVersion;
    [ObservableProperty] private string _playerName = string.Empty;
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private int _memoryMode;
    [ObservableProperty] private bool _useBmclApi;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _avatarInitials = "P";

    partial void OnPlayerNameChanged(string value)
    {
        AvatarInitials = string.IsNullOrWhiteSpace(value)
            ? "?"
            : value.Trim()[..1].ToUpperInvariant();
        SchedulePersist();
    }

    partial void OnSelectedVersionChanged(GameVersionItem? value)
    {
        if (value is not null)
        {
            _ = EnsureSuitableJavaSelectedAsync(value.Id);
            // Pre-warm IsLaunchReady in the background so the launch fast-path is instant.
            WarmLaunchReadyCache(value.Id);
        }

        LaunchGameCommand.NotifyCanExecuteChanged();
        SchedulePersist();
    }

    private void WarmLaunchReadyCache(string versionId)
    {
        var gameDir = _settingsLoaded ? SnapshotSettingsWithoutFlush().GameDirectory : null;

        // Pre-warm disk check (IsLaunchReady).
        if (!_launchReadyCache.ContainsKey(versionId))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var ready = GamePaths.IsLaunchReady(versionId, gameDir);
                    _launchReadyCache[versionId] = ready;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LaunchViewModel] WarmLaunchReadyCache failed: {ex.Message}");
                }
            });
        }

        // Pre-warm CmlLib IVersion JSON parse (~1500ms saved on next launch).
        if (_settingsLoaded && _launchService.IsValueCreated)
        {
            var settings = SnapshotSettingsWithoutFlush();
            _ = Task.Run(() => _launchService.Value.WarmVersionAsync(settings, versionId));
        }
    }
    partial void OnMaxRamMbChanged(int value) => SchedulePersist();
    partial void OnMemoryModeChanged(int value) => SchedulePersist();
    partial void OnUseBmclApiChanged(bool value)
    {
        SchedulePersist();
        if (_launchService.IsValueCreated)
            _launchService.Value.InvalidateVersionCache();
    }
    partial void OnJavaPathChanged(string? value) => SchedulePersist();

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (IsLocalReady)
            return;

        try
        {
            EnsureSettingsLoaded();

            // Disk + java -version probes stay off the UI thread (full Java scan can take seconds).
            var gameDir = SnapshotSettingsWithoutFlush().GameDirectory;
            var saved = _settings.SelectedVersion;
            var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

            ApplyInstalledVersions(items, saved);

            StatusText = Versions.Count > 0
                ? SelectedVersion?.Id ?? Loc.Get(LocKeys.Home_Ready)
                : Loc.Get(LocKeys.Home_GoDownload);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Home_InitFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    /// <summary>Refresh instance labels after the UI language changes.</summary>
    public void Relocalize()
    {
        foreach (var version in Versions)
            version.NotifyLocalization();

        RefreshJavaList();

        if (IsGameRunning)
            StatusText = Loc.Get(LocKeys.Home_GameRunning);
        else if (IsLaunching)
            StatusText = Loc.Format(LocKeys.Home_Preparing, SelectedVersion?.Id ?? string.Empty);
        else
            StatusText = Versions.Count > 0
                ? SelectedVersion?.Id ?? Loc.Get(LocKeys.Home_Ready)
                : Loc.Get(LocKeys.Home_GoDownload);
    }

    /// <summary>Drop cached Java argv after instance options that affect launch change.</summary>
    public void InvalidateLaunchProcessCache(string? versionId = null)
    {
        if (!_launchService.IsValueCreated)
            return;
        _launchService.Value.InvalidateLaunchProcessCache(versionId);
    }

    private void EnsureSettingsLoaded()
    {
        if (_settingsLoaded)
            return;

        _settings = _settingsService.Load();
        _suppressPersist = true;
        PlayerName = NameRules.ValidatePlayerName(_settings.PlayerName) is null
            ? _settings.PlayerName
            : string.Empty;
        MaxRamMb = _settings.MaxRamMb;
        MemoryMode = MemoryLaunchResolver.NormalizeDefaultMode(_settings) == DefaultMemoryMode.Dynamic
            ? (int)DefaultMemoryMode.Dynamic
            : (int)DefaultMemoryMode.Custom;
        UseBmclApi = _settings.UseBmclApi;
        JavaPath = _settings.JavaPath;
        AvatarInitials = string.IsNullOrWhiteSpace(PlayerName)
            ? "?"
            : PlayerName.Trim()[..1].ToUpperInvariant();
        _suppressPersist = false;
        _settingsLoaded = true;
    }

    /// <summary>Load settings without scanning local versions (Account page).</summary>
    public void EnsureSettingsReady() => EnsureSettingsLoaded();

    /// <summary>
    /// After OOBE (or any external settings write), keep the in-memory snapshot's
    /// language/theme in sync so the next Persist does not clobber them.
    /// </summary>
    public void SyncPersonalizationFromStore()
    {
        var fresh = _settingsService.Load();
        if (!_settingsLoaded)
        {
            _settings = fresh;
            return;
        }

        _settings.UiLanguage = fresh.UiLanguage ?? string.Empty;
        _settings.AppTheme = string.IsNullOrWhiteSpace(fresh.AppTheme) ? "Default" : fresh.AppTheme.Trim();
        _settings.HasCompletedOobe = fresh.HasCompletedOobe;
        _settings.AcceptedAboutLegalVersion = fresh.AcceptedAboutLegalVersion;
        _settings.SchemaVersion = Math.Max(_settings.SchemaVersion, fresh.SchemaVersion);
    }

    public async Task LoadLocalVersionsAsync()
    {
        StartupClock.Mark("LoadLocalVersions begin");
        EnsureSettingsLoaded();
        EnsureVersionsWatcher();
        var gameDir = SnapshotSettingsWithoutFlush().GameDirectory;
        var saved = _settings.SelectedVersion;
        var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

        ApplyInstalledVersions(items, saved);
        StartupClock.Mark($"LoadLocalVersions done ({items.Count} instances)");
        StartupClock.Flush();
    }

    /// <summary>Reload local instances and select the version that was just installed.</summary>
    public async Task SelectInstalledVersionAsync(string versionId)
    {
        EnsureSettingsLoaded();
        EnsureVersionsWatcher();
        var gameDir = SnapshotSettingsWithoutFlush().GameDirectory;
        var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

        ApplyInstalledVersions(items, versionId);
        PersistNow();
    }

    private void ApplyInstalledVersions(IReadOnlyList<GameVersionItem> items, string? preferredId)
    {
        // Invalidate pre-warmed results — the installed set has changed.
        _launchReadyCache.Clear();

        var ordered = OrderByInstancePreference(items, _settings.InstanceOrder);
        MergeVersionsCollection(ordered);

        // Preload icons into cache so opening or switching to Instances list is instant
        foreach (var v in ordered)
        {
            _ = v.IconImage;
        }

        _suppressPersist = true;
        SelectedVersion = Versions.FirstOrDefault(v =>
                              !string.IsNullOrEmpty(preferredId) &&
                              string.Equals(v.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                          ?? Versions.FirstOrDefault();
        _suppressPersist = false;
        IsLocalReady = true;
    }

    /// <summary>Diff-update <see cref="Versions"/> instead of Clear+Add (avoids ListView binding storms).</summary>
    private void MergeVersionsCollection(IReadOnlyList<GameVersionItem> ordered)
    {
        var wanted = new HashSet<string>(
            ordered.Select(v => v.Id),
            StringComparer.OrdinalIgnoreCase);

        for (var i = Versions.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Versions[i].Id))
                Versions.RemoveAt(i);
        }

        var existing = new Dictionary<string, GameVersionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Versions)
            existing[v.Id] = v;

        for (var i = 0; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (existing.TryGetValue(next.Id, out var cur))
            {
                cur.IsInstalled = next.IsInstalled;
                cur.Notes = next.Notes;
                cur.IconGlyph = next.IconGlyph;
                if (!string.Equals(cur.IconPath, next.IconPath, StringComparison.OrdinalIgnoreCase))
                {
                    cur.IconPath = next.IconPath;
                    cur.RefreshIcon();
                }

                if (next.OfficialJavaMajor is int major)
                    cur.OfficialJavaMajor = major;

                var at = Versions.IndexOf(cur);
                if (at >= 0 && at != i)
                    Versions.Move(at, i);
            }
            else
            {
                Versions.Insert(Math.Min(i, Versions.Count), next);
                existing[next.Id] = next;
            }
        }
    }

    /// <summary>Persist the current <see cref="Versions"/> order after a drag-reorder.</summary>
    public void PersistInstanceOrder()
    {
        EnsureSettingsLoaded();
        _settings.InstanceOrder = Versions.Select(v => v.Id).ToList();
        PersistNow();
    }

    private static List<GameVersionItem> OrderByInstancePreference(
        IReadOnlyList<GameVersionItem> items,
        IReadOnlyList<string>? order)
    {
        if (items.Count == 0)
            return [];

        if (order is null || order.Count == 0)
            return items.ToList();

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            if (!string.IsNullOrWhiteSpace(id) && !rank.ContainsKey(id))
                rank[id] = rank.Count;
        }

        return items
            .OrderBy(v => rank.TryGetValue(v.Id, out var r) ? r : int.MaxValue)
            .ThenByDescending(v => v.ReleaseTime ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private void EnsureVersionsWatcher()
    {
        try
        {
            var root = GamePaths.GetVersionsRoot(SnapshotSettingsWithoutFlush().GameDirectory);
            Directory.CreateDirectory(root);

            if (_versionsWatcher is not null &&
                string.Equals(_versionsWatcherPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeVersionsWatcher();

            var watcher = new FileSystemWatcher(root)
            {
                NotifyFilter = NotifyFilters.DirectoryName |
                               NotifyFilters.FileName |
                               NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024
            };
            watcher.Created += OnVersionsFileSystemEvent;
            watcher.Deleted += OnVersionsFileSystemEvent;
            watcher.Changed += OnVersionsFileSystemEvent;
            watcher.Renamed += OnVersionsFileSystemEvent;
            watcher.Error += (_, e) =>
                Debug.WriteLine($"[Launch] Versions watcher error: {e.GetException().Message}");
            watcher.EnableRaisingEvents = true;

            _versionsWatcher = watcher;
            _versionsWatcherPath = root;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launch] Versions watcher setup failed: {ex.Message}");
        }
    }

    private void DisposeVersionsWatcher()
    {
        if (_versionsWatcher is null)
            return;

        try
        {
            _versionsWatcher.EnableRaisingEvents = false;
            _versionsWatcher.Created -= OnVersionsFileSystemEvent;
            _versionsWatcher.Deleted -= OnVersionsFileSystemEvent;
            _versionsWatcher.Changed -= OnVersionsFileSystemEvent;
            _versionsWatcher.Renamed -= OnVersionsFileSystemEvent;
            _versionsWatcher.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launch] Versions watcher dispose failed: {ex.Message}");
        }

        _versionsWatcher = null;
        _versionsWatcherPath = null;
    }

    private void OnVersionsFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!ShouldReloadForVersionsEvent(e))
            return;

        _dispatcher.TryEnqueue(() =>
        {
            if (_versionsReloadTimer is null)
                return;
            _versionsReloadTimer.Stop();
            _versionsReloadTimer.Start();
        });
    }

    private static bool ShouldReloadForVersionsEvent(FileSystemEventArgs e)
    {
        var relative = e.Name;
        if (string.IsNullOrWhiteSpace(relative))
            return true;

        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // versions/{id} created / deleted / renamed
        if (parts.Length == 1)
            return true;

        // versions/{id}/markers or profile json
        if (parts.Length == 2)
        {
            var file = parts[1];
            if (string.Equals(file, GamePaths.UserInstanceMarker, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, GamePaths.DependencyMarker, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, GamePaths.ReadyMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task ReloadLocalVersionsFromWatcherAsync()
    {
        if (Interlocked.Exchange(ref _versionsReloadBusy, 1) == 1)
            return;

        try
        {
            await LoadLocalVersionsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launch] Watcher reload failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _versionsReloadBusy, 0);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchGameAsync()
    {
        if (SelectedVersion is null || IsLaunching)
            return;

        var active = _accounts.GetActive();
        if (active is null)
        {
            StatusText = Loc.Get(LocKeys.Account_NeedLogin);
            return;
        }

        if (active.Kind == AccountKind.Offline)
        {
            if (!_accounts.HasLicensedAccount)
            {
                StatusText = Loc.Get(LocKeys.Account_OfflineLockedHint);
                return;
            }

            var nameError = NameRules.ValidatePlayerName(active.DisplayName);
            if (nameError is not null)
            {
                StatusText = nameError;
                return;
            }
        }
        else if (active.Kind != AccountKind.Microsoft ||
                 string.IsNullOrWhiteSpace(active.MicrosoftAccountId))
        {
            StatusText = Loc.Get(LocKeys.Account_MicrosoftRefreshFailed);
            return;
        }

        PlayerName = active.DisplayName.Trim();

        var cts = new CancellationTokenSource();
        lock (_launchCtsGate)
            _launchCtsList.Add(cts);
        var token = cts.Token;

        Interlocked.Increment(ref _launchingCount);
        IsLaunching = true;
        IsIndeterminate = true;
        ProgressValue = 0;
        var versionId = SelectedVersion.Id;
        StatusText = Loc.Format(LocKeys.Home_Preparing, versionId);
        LaunchGameCommand.NotifyCanExecuteChanged();

        var uiProgress = new CoalescedUiProgress(_dispatcher, (status, progress, indeterminate) =>
        {
            IsIndeterminate = indeterminate;
            ProgressValue = progress;
            if (!string.IsNullOrEmpty(status))
                StatusText = status;
        });

        IProgress<FileProgressInfo> fileProgress = new DirectProgress<FileProgressInfo>(e =>
            uiProgress.ReportFile(
                string.IsNullOrWhiteSpace(e.Name) ? Loc.Get(LocKeys.Progress_FileFallback) : e.Name,
                e.ProgressedTasks,
                e.TotalTasks));
        IProgress<ByteProgressInfo> byteProgress = new DirectProgress<ByteProgressInfo>(e =>
        {
            if (e.TotalBytes <= 0)
                return;

            uiProgress.Report(
                Loc.Format(
                    LocKeys.Home_DownloadingBytes,
                    FormatBytes(e.ProgressedBytes),
                    FormatBytes(e.TotalBytes)),
                e.ProgressedBytes * 100.0 / e.TotalBytes,
                indeterminate: false);
        });

        Process? thisProcess = null;
        try
        {
            OnlineLaunchSession? onlineSession = null;
            if (active.Kind == AccountKind.Microsoft)
            {
                StatusText = Loc.Get(LocKeys.Account_MicrosoftSigningIn);
                onlineSession = await ResolveMicrosoftLaunchSessionAsync(active, token)
                    .ConfigureAwait(true);
                PlayerName = onlineSession.Username;
            }

            var settings = SnapshotSettings();
            Persist(settings);
            var offlineSkin = onlineSession is null ? ResolveOfflineSkinOptions() : null;

            thisProcess = await Task.Run(
                    () => _launchService.Value.LaunchAsync(
                        settings,
                        SelectedVersion.Id,
                        PlayerName,
                        fileProgress,
                        byteProgress,
                        token,
                        offlineSkin,
                        onlineSession),
                    token)
                .ConfigureAwait(true);

            // LaunchAsync may have started the process before noticing cancel.
            if (token.IsCancellationRequested)
            {
                TryKillProcessSafe(thisProcess);
                token.ThrowIfCancellationRequested();
            }

            _gameProcess = thisProcess;
            TrackGameProcess(thisProcess, versionId, settings.GameDirectory);
            _statsStore.RecordSessionStart(versionId, settings.GameDirectory);

            // Persist auto-downloaded / resolved Java path.
            if (!string.IsNullOrWhiteSpace(settings.JavaPath) &&
                !string.Equals(JavaPath, settings.JavaPath, StringComparison.OrdinalIgnoreCase))
            {
                _suppressPersist = true;
                JavaPath = settings.JavaPath;
                _suppressPersist = false;
            }

            Persist(settings);

            // Prime the IVersion cache so the next launch of this version is instant.
            var launchedVersionId = versionId;
            var settingsForWarm = settings;
            _ = Task.Run(() => _launchService.Value.WarmVersionAsync(settingsForWarm, launchedVersionId));

            // Process is running — keep launch UI visible until the game window appears.
            IsIndeterminate = true;
            ProgressValue = 0;
            StatusText = Loc.Get(LocKeys.Home_WaitingForWindow);

            await WaitForGameWindowAsync(thisProcess, TimeSpan.FromSeconds(60), token)
                .ConfigureAwait(true);

            if (thisProcess.HasExited)
            {
                RefreshGameRunningState();
                UpdateRunningStatusText(exited: true);
                ProgressValue = 0;
            }
            else
            {
                ApplyWindowTitle(thisProcess, versionId, PlayerName, settings.GameDirectory);
                IsIndeterminate = false;
                ProgressValue = 100;
                UpdateRunningStatusText(exited: false);
                ApplyPostLaunchWindowAction(settings);
                MaybeOpenGameLogViewer(settings, versionId, settings.GameDirectory);
                _ = WatchProcessAsync(thisProcess, versionId, settings.GameDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            TryKillProcessSafe(thisProcess);
            RefreshGameRunningState();
            IsIndeterminate = false;
            ProgressValue = 0;
            StatusText = IsGameRunning
                ? FormatRunningGamesStatus()
                : Loc.Get(LocKeys.Home_Cancelled);
        }
        catch (Exception ex)
        {
            IsIndeterminate = false;
            ProgressValue = 0;
            StatusText = Loc.Format(LocKeys.Home_LaunchFailed, ex.Message);
            Debug.WriteLine($"[LaunchViewModel] {ex}");
        }
        finally
        {
            lock (_launchCtsGate)
                _launchCtsList.Remove(cts);
            try { cts.Dispose(); } catch { /* ignore */ }

            if (Interlocked.Decrement(ref _launchingCount) <= 0)
            {
                _launchingCount = 0;
                IsLaunching = false;
                IsIndeterminate = false;
                ProgressValue = 0;
            }

            LaunchGameCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelLaunch()
    {
        List<CancellationTokenSource> snapshot;
        lock (_launchCtsGate)
            snapshot = _launchCtsList.ToList();

        foreach (var cts in snapshot)
        {
            try { cts.Cancel(); }
            catch { /* ignore */ }
        }

        if (_gameProcess is { HasExited: false } proc)
        {
            TryKillProcessSafe(proc);
        }

        StatusText = Loc.Get(LocKeys.Home_Cancelling);
    }

    [RelayCommand(CanExecute = nameof(CanStopAllGames))]
    private void StopAllGames()
    {
        List<CancellationTokenSource> snapshot;
        lock (_launchCtsGate)
            snapshot = _launchCtsList.ToList();
        foreach (var cts in snapshot)
        {
            try { cts.Cancel(); }
            catch { /* ignore */ }
        }

        KillAllTrackedProcesses();
        RefreshGameRunningState();
        StatusText = Loc.Get(LocKeys.Instances_GamesStopped);
    }

    private bool CanStopAllGames() => IsGameRunning;

    private void TryKillGameProcess()
    {
        KillAllTrackedProcesses();
        RefreshGameRunningState();
    }

    private void TryKillProcessSafe(Process? process)
    {
        if (process is null)
            return;

        lock (_processGate)
        {
            try { _forceKilledProcessIds.Add(process.Id); }
            catch { /* ignore */ }
        }

        TryKillProcess(process);
    }

    private void ApplyWindowTitle(Process process, string versionId, string playerName, string? gameDirectory)
    {
        try
        {
            var title = _instanceSettings.Load(versionId, gameDirectory).WindowTitle;
            GameWindowTitle.TryApply(process, title, versionId, playerName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Window title: {ex.Message}");
        }
    }

    private void UpdateRunningStatusText(bool exited)
    {
        if (exited && !IsGameRunning)
        {
            StatusText = Loc.Get(LocKeys.Home_GameExited);
            return;
        }

        StatusText = FormatRunningGamesStatus();
    }

    private string FormatRunningGamesStatus()
    {
        var count = RunningGamesCount > 0 ? RunningGamesCount : CountRunningProcesses();
        return count <= 1
            ? Loc.Get(LocKeys.Home_GameRunning)
            : Loc.Format(LocKeys.Instances_GamesRunning, count);
    }

    private int CountRunningProcesses()
    {
        lock (_processGate)
        {
            var n = 0;
            foreach (var process in _gameProcesses)
            {
                try
                {
                    if (!process.HasExited)
                        n++;
                }
                catch
                {
                    // skip
                }
            }

            return n;
        }
    }

    private void TrackGameProcess(Process? process, string? versionId = null, string? gameDirectory = null)
    {
        if (process is null)
            return;

        lock (_processGate)
        {
            if (!_gameProcesses.Contains(process))
                _gameProcesses.Add(process);

            try
            {
                _processSessions[process.Id] = new TrackedGameSession(
                    versionId ?? SelectedVersion?.Id ?? string.Empty,
                    gameDirectory ?? GamePaths.GetMinecraftRoot(),
                    DateTimeOffset.UtcNow);
            }
            catch
            {
                // process may have already exited
            }
        }

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited -= OnTrackedProcessExited;
            process.Exited += OnTrackedProcessExited;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Track failed: {ex.Message}");
        }

        RefreshGameRunningState();
    }

    private void OnTrackedProcessExited(object? sender, EventArgs e) =>
        RunOnUi(() =>
        {
            var process = sender as Process;
            TrackedGameSession? session = null;
            var exitCode = 0;
            var wasForceKilled = false;
            try
            {
                if (process is not null)
                {
                    exitCode = process.ExitCode;
                    lock (_processGate)
                    {
                        _processSessions.Remove(process.Id, out session);
                        wasForceKilled = _forceKilledProcessIds.Remove(process.Id);
                    }
                }
            }
            catch
            {
                // ignore
            }

            PruneExitedProcesses();
            RefreshGameRunningState();

            // Always analyze the exited session (even if another instance is still running).
            if (session is not null)
            {
                _statsStore.RecordSessionEnd(session.VersionId, session.GameDirectory, exitCode);
                _ = MaybeRunCrashAnalysisAsync(session, exitCode, wasForceKilled);
            }

            if (!IsGameRunning && !IsLaunching)
            {
                StatusText = Loc.Get(LocKeys.Home_GameExited);
                ProgressValue = 0;
                App.MainWindowInstance?.Activate();
                RestoreWindowAfterMinimizeUntilExit();
                // Do NOT Close() the log window here — closing a secondary WinUI Window
                // while it is focused often tears down the whole process.
                DetachGameLogWindow();
                GameExited?.Invoke(this, EventArgs.Empty);
            }
            else if (IsGameRunning)
            {
                StatusText = FormatRunningGamesStatus();
            }
        });

    private async Task MaybeRunCrashAnalysisAsync(TrackedGameSession session, int exitCode, bool wasForceKilled)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(session.VersionId))
                return;

            var settings = _settingsService.Load();
            if (!settings.CrashAnalysisEnabled)
                return;

            // Wait briefly and retry — crash-reports are often written after process exit.
            var facts = await CrashEvidenceCollector.CollectWithRetryAsync(
                session.VersionId,
                session.GameDirectory,
                session.StartedAt).ConfigureAwait(true);

            var kind = CrashExitGate.ClassifyExit(exitCode, wasForceKilled, facts);
            if (!CrashExitGate.ShouldAnalyze(kind, settings))
                return;

            var request = new CrashAnalysisRequest
            {
                VersionId = session.VersionId,
                GameDirectory = session.GameDirectory,
                ExitCode = exitCode,
                ExitKind = kind,
                SessionStartedAt = session.StartedAt
            };

            var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
            await CrashAnalyzer.MaybeAnalyzeAndPresentAsync(request, settings, xamlRoot, facts).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] crash analysis: {ex}");
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // Mark before Kill so Exited can distinguish Ardel force-kill.
                // Caller must register via MarkForceKilled when holding _processGate.
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Kill failed: {ex.Message}");
        }
    }

    private void KillAllTrackedProcesses()
    {
        List<Process> snapshot;
        lock (_processGate)
        {
            snapshot = _gameProcesses.ToList();
            foreach (var process in snapshot)
            {
                try { _forceKilledProcessIds.Add(process.Id); }
                catch { /* ignore */ }
            }

            if (_gameProcess is not null)
            {
                try { _forceKilledProcessIds.Add(_gameProcess.Id); }
                catch { /* ignore */ }
            }
        }

        foreach (var process in snapshot)
            TryKillProcess(process);

        if (_gameProcess is not null && !snapshot.Contains(_gameProcess))
            TryKillProcess(_gameProcess);

        PruneExitedProcesses();
    }

    private void PruneExitedProcesses()
    {
        lock (_processGate)
        {
            for (var i = _gameProcesses.Count - 1; i >= 0; i--)
            {
                var process = _gameProcesses[i];
                var exited = false;
                try
                {
                    exited = process.HasExited;
                }
                catch
                {
                    exited = true;
                }

                if (!exited)
                    continue;

                try
                {
                    process.Exited -= OnTrackedProcessExited;
                }
                catch
                {
                    // ignore
                }

                try
                {
                    _processSessions.Remove(process.Id);
                }
                catch
                {
                    // ignore
                }

                _gameProcesses.RemoveAt(i);
            }
        }
    }

    private void RefreshGameRunningState()
    {
        PruneExitedProcesses();

        var running = 0;
        lock (_processGate)
        {
            foreach (var process in _gameProcesses)
            {
                try
                {
                    if (!process.HasExited)
                        running++;
                }
                catch
                {
                    // treat as dead
                }
            }
        }

        RunningGamesCount = running;
        var any = running > 0;
        if (IsGameRunning != any)
            IsGameRunning = any;

        UpdateGameWatchTimer(any);
    }

    private void UpdateGameWatchTimer(bool running)
    {
        if (running)
        {
            if (_gameWatchTimer is null)
            {
                _gameWatchTimer = _dispatcher.CreateTimer();
                _gameWatchTimer.IsRepeating = true;
                _gameWatchTimer.Interval = TimeSpan.FromSeconds(1);
                _gameWatchTimer.Tick += (_, _) => RefreshGameRunningState();
            }

            if (!_gameWatchTimerRunning)
            {
                _gameWatchTimer.Start();
                _gameWatchTimerRunning = true;
            }

            return;
        }

        if (_gameWatchTimer is not null && _gameWatchTimerRunning)
        {
            _gameWatchTimer.Stop();
            _gameWatchTimerRunning = false;
        }
    }

    private bool CanLaunch()
    {
        if (IsLaunching || SelectedVersion is null)
            return false;

        // Keep clickable when unsigned-in — callers show a dialog; this is a final guard.
        return true;
    }

    private async Task<OnlineLaunchSession> ResolveMicrosoftLaunchSessionAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.MicrosoftAccountId))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftRefreshFailed));

        var session = await _microsoftAuth
            .AuthenticateSilentlyAsync(account.MicrosoftAccountId, cancellationToken)
            .ConfigureAwait(false);

        var username = session.Username ?? account.DisplayName;
        var uuid = (session.UUID ?? string.Empty)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(uuid) ||
            string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftRefreshFailed));

        if (!string.Equals(account.DisplayName, username, StringComparison.Ordinal) ||
            !string.Equals(
                (account.Uuid ?? string.Empty).Replace("-", "", StringComparison.Ordinal),
                uuid,
                StringComparison.OrdinalIgnoreCase))
        {
            account.DisplayName = username;
            account.Uuid = uuid;
            _accounts.Update(account);
        }

        return new OnlineLaunchSession(username, uuid, session.AccessToken);
    }

    private OfflineSkinLaunchOptions? ResolveOfflineSkinOptions()
    {
        var active = _accounts.GetActive();
        if (active is null || active.Kind != AccountKind.Offline)
            return null;

        var skin = _skins.Find(active.SkinId);
        if (skin is null || (!skin.IsBuiltIn && !skin.IsConfigured))
            return null;

        var path = _skins.GetAbsolutePath(skin);
        if (!File.Exists(path))
            return null;

        var uuid = string.IsNullOrWhiteSpace(active.Uuid)
            ? OfflinePlayerUuid.FromPlayerName(active.DisplayName.Trim())
            : active.Uuid;

        return new OfflineSkinLaunchOptions(
            uuid,
            active.DisplayName.Trim(),
            path,
            skin.ArmModel == SkinArmModel.Slim);
    }

    public bool ShowStopGameButton => IsGameRunning && !IsLaunching;

    private bool CanCancel() => IsLaunching;

    partial void OnIsGameRunningChanged(bool value)
    {
        StopAllGamesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowStopGameButton));
    }

    /// <summary>Select an installed version and start it (from Instances page).</summary>
    public Task LaunchVersionAsync(GameVersionItem version)
    {
        ArgumentNullException.ThrowIfNull(version);
        SelectedVersion = Versions.FirstOrDefault(v =>
                               string.Equals(v.Id, version.Id, StringComparison.OrdinalIgnoreCase))
                           ?? version;
        if (!Versions.Contains(SelectedVersion))
            Versions.Insert(0, SelectedVersion);

        return LaunchGameAsync();
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        LaunchGameCommand.NotifyCanExecuteChanged();
        CancelLaunchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowStopGameButton));
    }

    public void RefreshJavaList()
    {
        JavaInstallations.Clear();
        foreach (var java in JavaLocator.FindInstallations())
            JavaInstallations.Add(java);

        if (SelectedVersion is not null)
            _ = EnsureSuitableJavaSelectedAsync(SelectedVersion.Id);
    }

    /// <summary>
    /// Prefer a local Java that meets the required major. Never keep / pick a too-old install.
    /// Empty path means launch will download a matching Temurin runtime.
    /// Uses official <c>javaVersion.majorVersion</c> when already cached/local; otherwise waits for launch.
    /// </summary>
    private async Task EnsureSuitableJavaSelectedAsync(string? versionId = null, int? requiredMajor = null)
    {
        var probeId = Interlocked.Increment(ref _javaProbeGeneration);
        var currentPath = JavaPath;
        var installations = JavaInstallations.ToList();

        string? best;
        try
        {
            best = await Task.Run(() =>
            {
                int required;
                if (requiredMajor is int explicitMajor)
                {
                    required = explicitMajor;
                }
                else if (!string.IsNullOrWhiteSpace(versionId))
                {
                    if (OfficialJavaRequirements.TryGetCached(versionId, out var cached))
                        required = cached;
                    else if (OfficialJavaRequirements.TryReadLocal(versionId) is int local)
                        required = local;
                    else
                        return currentPath;
                }
                else
                {
                    return currentPath;
                }

                if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                {
                    try
                    {
                        var actual = JavaLocator.GetJavaVersion(currentPath);
                        if (JavaLocator.IsCompatible(actual, required))
                            return currentPath;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LaunchViewModel] Java probe failed: {ex.Message}");
                    }
                }

                return JavaLocator.FindBestMatch(required, installations)?.JavaExePath
                       ?? JavaRuntimeInstaller.TryFindInstalled(required);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Java select failed: {ex.Message}");
            return;
        }

        if (probeId != Volatile.Read(ref _javaProbeGeneration))
            return;

        if (string.Equals(best, JavaPath, StringComparison.OrdinalIgnoreCase))
            return;

        _suppressPersist = true;
        JavaPath = best;
        _suppressPersist = false;
    }

    public LauncherSettings SnapshotSettings()
    {
        EnsureSettingsLoaded();
        // Flush any debounced edits before launch / install snapshots.
        PersistNow();
        _settings.PlayerName = PlayerName;
        _settings.MaxRamMb = MaxRamMb;
        _settings.MemoryMode = MemoryMode == (int)DefaultMemoryMode.Dynamic
            ? (int)DefaultMemoryMode.Dynamic
            : (int)DefaultMemoryMode.Custom;
        _settings.UseBmclApi = UseBmclApi;
        _settings.JavaPath = JavaPath;
        _settings.SelectedVersion = SelectedVersion?.Id;
        _settings.GameDirectory = GamePaths.GetMinecraftRoot();
        _settings.ForceVersionIsolation = true;
        // Language / theme live on the same settings object 鈥?never clear them on flush.
        _settings.UiLanguage ??= string.Empty;
        _settings.AppTheme = string.IsNullOrWhiteSpace(_settings.AppTheme) ? "Default" : _settings.AppTheme;
        return _settings;
    }

    private void SchedulePersist()
    {
        if (_suppressPersist || !_settingsLoaded)
            return;

        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void Persist(LauncherSettings? settings = null)
    {
        if (settings is not null)
        {
            PersistNow(settings);
            return;
        }

        SchedulePersist();
    }

    private void PersistNow(LauncherSettings? settings = null)
    {
        if (_suppressPersist || !_settingsLoaded)
            return;

        _persistTimer.Stop();

        try
        {
            _settingsService.Save(settings ?? SnapshotSettingsWithoutFlush());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Persist failed: {ex}");
        }
    }

    public LauncherSettings SnapshotSettingsWithoutFlush()
    {
        EnsureSettingsLoaded();
        _settings.PlayerName = PlayerName;
        _settings.MaxRamMb = MaxRamMb;
        _settings.MemoryMode = MemoryMode == (int)DefaultMemoryMode.Dynamic
            ? (int)DefaultMemoryMode.Dynamic
            : (int)DefaultMemoryMode.Custom;
        _settings.UseBmclApi = UseBmclApi;
        _settings.JavaPath = JavaPath;
        _settings.SelectedVersion = SelectedVersion?.Id;
        _settings.GameDirectory = GamePaths.GetMinecraftRoot();
        _settings.ForceVersionIsolation = true;
        _settings.UiLanguage ??= string.Empty;
        _settings.AppTheme = string.IsNullOrWhiteSpace(_settings.AppTheme) ? "Default" : _settings.AppTheme;
        return _settings;
    }

    /// <summary>
    /// Wait untill the game process exposes a main window, exits, or the timeout elapses.
    /// </summary>
    private static async Task WaitForGameWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (process.HasExited)
                    return;

                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                    return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
                return;

            await Task.Delay(250, cancellationToken).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task WatchProcessAsync(Process process, string versionId, string gameDirectory)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                PruneExitedProcesses();
                RefreshGameRunningState();
                if (!IsGameRunning && !IsLaunching)
                {
                    StatusText = Loc.Get(LocKeys.Home_GameExited);
                    ProgressValue = 0;
                    App.MainWindowInstance?.Activate();
                    RestoreWindowAfterMinimizeUntilExit();
                    // Do NOT Close() the log window — secondary Window.Close() can exit the app.
                    DetachGameLogWindow();
                    GameExited?.Invoke(this, EventArgs.Empty);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Process watch error: {ex}");
            RunOnUi(() => StatusText = Loc.Format(LocKeys.Home_ProcessError, ex.Message));
        }
    }

    private void ApplyPostLaunchWindowAction(LauncherSettings settings)
    {
        try
        {
            var action = (PostLaunchWindowAction)settings.PostLaunchWindowAction;
            var window = App.MainWindowInstance;
            if (window is null)
                return;

            switch (action)
            {
                case PostLaunchWindowAction.Minimize:
                    MinimizeMainWindow(window);
                    break;
                case PostLaunchWindowAction.MinimizeUntilExit:
                    _minimizeUntilExitActive = true;
                    MinimizeMainWindow(window);
                    break;
                case PostLaunchWindowAction.ExitLauncher:
                    // Re-check persisted preference — ComboBox TwoWay bugs used to flip this accidentally.
                    if (_settingsService.Load().PostLaunchWindowAction !=
                        (int)PostLaunchWindowAction.ExitLauncher)
                    {
                        Debug.WriteLine("[LaunchViewModel] Ignoring ExitLauncher; settings say otherwise.");
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(400).ConfigureAwait(false);
                        RunOnUi(() =>
                        {
                            try { window.Close(); }
                            catch (Exception ex) { Debug.WriteLine(ex); }
                        });
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Post-launch action: {ex.Message}");
        }
    }

    private static void MinimizeMainWindow(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
            // OverlappedPresenter.Minimize is the WinUI path.
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                presenter.Minimize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Minimize failed: {ex.Message}");
        }
    }

    private void RestoreWindowAfterMinimizeUntilExit()
    {
        if (!_minimizeUntilExitActive)
            return;
        _minimizeUntilExitActive = false;
        try
        {
            var window = App.MainWindowInstance;
            if (window is null)
                return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                presenter.Restore();
            window.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Restore failed: {ex.Message}");
        }
    }

    private void MaybeOpenGameLogViewer(LauncherSettings settings, string versionId, string gameDirectory)
    {
        if (!settings.OpenGameLogViewer)
            return;

        try
        {
            var instanceDir = GamePaths.EnsureVersionIsolation(versionId, gameDirectory);
            try
            {
                if (_gameLogWindow is { IsDisposed: false })
                {
                    _gameLogWindow.Close();
                    _gameLogWindow.Dispose();
                }
            }
            catch { /* ignore */ }

            _gameLogWindow = new Views.GameLogWindow(instanceDir);
            _gameLogWindow.ActivateIndependent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Game log viewer: {ex.Message}");
        }
    }

    /// <summary>Stop pointing at the viewer; leave the separate process open for reading.</summary>
    private void DetachGameLogWindow()
    {
        try { _gameLogWindow?.StopTailing(); } catch { /* ignore */ }
        try { _gameLogWindow?.Dispose(); } catch { /* ignore */ }
        _gameLogWindow = null;
    }

    private sealed record TrackedGameSession(
        string VersionId,
        string GameDirectory,
        DateTimeOffset StartedAt);

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }

    private static string FormatBytes(long bytes)
    {
        string[] units =
        [
            Loc.Get(LocKeys.Unit_Byte),
            Loc.Get(LocKeys.Unit_Kilobyte),
            Loc.Get(LocKeys.Unit_Megabyte),
            Loc.Get(LocKeys.Unit_Gigabyte)
        ];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
