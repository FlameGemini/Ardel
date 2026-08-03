using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly Lazy<IMinecraftLaunchService> _launchService;
    private readonly LocalVersionStore _localVersions;
    private readonly SettingsService _settingsService;
    private readonly AccountStore _accounts;
    private readonly SkinLibraryStore _skins;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _persistTimer;
    private LauncherSettings _settings = new();
    private CancellationTokenSource? _launchCts;
    private Process? _gameProcess;
    private readonly object _processGate = new();
    private readonly List<Process> _gameProcesses = [];
    private DispatcherQueueTimer? _gameWatchTimer;
    private bool _gameWatchTimerRunning;
    private bool _suppressPersist = true;
    private bool _localReady;
    private bool _settingsLoaded;
    private int _javaProbeGeneration;

    public LaunchViewModel(
        Lazy<IMinecraftLaunchService> launchService,
        LocalVersionStore localVersions,
        SettingsService settingsService,
        AccountStore accounts,
        SkinLibraryStore skins,
        DispatcherQueue dispatcher)
    {
        _launchService = launchService;
        _localVersions = localVersions;
        _settingsService = settingsService;
        _accounts = accounts;
        _skins = skins;
        _dispatcher = dispatcher;
        _accounts.Changed += (_, _) =>
            _dispatcher.TryEnqueue(() => LaunchGameCommand.NotifyCanExecuteChanged());
        _persistTimer = dispatcher.CreateTimer();
        _persistTimer.IsRepeating = false;
        _persistTimer.Interval = TimeSpan.FromMilliseconds(400);
        _persistTimer.Tick += (_, _) => PersistNow();
        // No disk I/O / CmlLib in ctor — first paint stays light
    }

    public ObservableCollection<GameVersionItem> Versions { get; } = [];
    public ObservableCollection<JavaInstallation> JavaInstallations { get; } = [];

    [ObservableProperty] private GameVersionItem? _selectedVersion;
    [ObservableProperty] private string _playerName = string.Empty;
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxRamMb = 4096;
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
            _ = EnsureSuitableJavaSelectedAsync(value.Id);

        SchedulePersist();
    }
    partial void OnMaxRamMbChanged(int value) => SchedulePersist();
    partial void OnUseBmclApiChanged(bool value) => SchedulePersist();
    partial void OnJavaPathChanged(string? value) => SchedulePersist();

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_localReady)
            return;

        try
        {
            EnsureSettingsLoaded();

            // Disk + java -version probes stay off the UI thread (full Java scan can take seconds).
            var gameDir = SnapshotSettings().GameDirectory;
            var saved = _settings.SelectedVersion;
            var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

            Versions.Clear();
            foreach (var item in items)
                Versions.Add(item);

            _suppressPersist = true;
            SelectedVersion = Versions.FirstOrDefault(v =>
                                  string.Equals(v.Id, saved, StringComparison.OrdinalIgnoreCase))
                              ?? Versions.FirstOrDefault();
            _suppressPersist = false;

            _localReady = true;
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

    public async Task LoadLocalVersionsAsync()
    {
        EnsureSettingsLoaded();
        var gameDir = SnapshotSettings().GameDirectory;
        var saved = _settings.SelectedVersion;
        var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

        ApplyInstalledVersions(items, saved);
    }

    /// <summary>Reload local instances and select the version that was just installed.</summary>
    public async Task SelectInstalledVersionAsync(string versionId)
    {
        EnsureSettingsLoaded();
        var gameDir = SnapshotSettings().GameDirectory;
        var items = await Task.Run(() => _localVersions.GetInstalled(gameDir)).ConfigureAwait(true);

        ApplyInstalledVersions(items, versionId);
        PersistNow();
    }

    private void ApplyInstalledVersions(IReadOnlyList<GameVersionItem> items, string? preferredId)
    {
        Versions.Clear();
        foreach (var item in items)
            Versions.Add(item);

        _suppressPersist = true;
        SelectedVersion = Versions.FirstOrDefault(v =>
                              !string.IsNullOrEmpty(preferredId) &&
                              string.Equals(v.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                          ?? Versions.FirstOrDefault();
        _suppressPersist = false;
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

        if (active.Kind == AccountKind.Microsoft)
        {
            StatusText = Loc.Get(LocKeys.Account_MicrosoftComingSoon);
            return;
        }

        var nameError = NameRules.ValidatePlayerName(active.DisplayName);
        if (nameError is not null)
        {
            StatusText = nameError;
            return;
        }

        PlayerName = active.DisplayName.Trim();

        _launchCts?.Cancel();
        _launchCts = new CancellationTokenSource();
        var token = _launchCts.Token;

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

        try
        {
            var settings = SnapshotSettings();
            Persist(settings);
            var offlineSkin = ResolveOfflineSkinOptions();

            _gameProcess = await Task.Run(
                    () => _launchService.Value.LaunchAsync(
                        settings,
                        SelectedVersion.Id,
                        PlayerName,
                        fileProgress,
                        byteProgress,
                        token,
                        offlineSkin),
                    token)
                .ConfigureAwait(true);

            // LaunchAsync may have started the process before noticing cancel.
            if (token.IsCancellationRequested)
            {
                TryKillGameProcess();
                token.ThrowIfCancellationRequested();
            }

            TrackGameProcess(_gameProcess);

            // Persist auto-downloaded / resolved Java path.
            if (!string.IsNullOrWhiteSpace(settings.JavaPath) &&
                !string.Equals(JavaPath, settings.JavaPath, StringComparison.OrdinalIgnoreCase))
            {
                _suppressPersist = true;
                JavaPath = settings.JavaPath;
                _suppressPersist = false;
            }

            Persist(settings);

            // Process is running — keep launch UI visible until the game window appears.
            IsIndeterminate = true;
            ProgressValue = 0;
            StatusText = Loc.Get(LocKeys.Home_WaitingForWindow);

            await WaitForGameWindowAsync(_gameProcess, TimeSpan.FromSeconds(60), token)
                .ConfigureAwait(true);

            if (_gameProcess.HasExited)
            {
                RefreshGameRunningState();
                StatusText = Loc.Get(LocKeys.Home_GameExited);
                ProgressValue = 0;
            }
            else
            {
                IsIndeterminate = false;
                ProgressValue = 100;
                StatusText = Loc.Get(LocKeys.Home_GameRunning);
                _ = WatchProcessAsync(_gameProcess);
            }
        }
        catch (OperationCanceledException)
        {
            TryKillGameProcess();
            StatusText = Loc.Get(LocKeys.Home_Cancelled);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Home_LaunchFailed, ex.Message);
            Debug.WriteLine($"[LaunchViewModel] {ex}");
        }
        finally
        {
            IsLaunching = false;
            IsIndeterminate = false;
            LaunchGameCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelLaunch()
    {
        _launchCts?.Cancel();
        StatusText = Loc.Get(LocKeys.Home_Cancelling);
        TryKillGameProcess();
    }

    [RelayCommand(CanExecute = nameof(CanStopAllGames))]
    private void StopAllGames()
    {
        _launchCts?.Cancel();
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

    private void TrackGameProcess(Process? process)
    {
        if (process is null)
            return;

        lock (_processGate)
        {
            if (!_gameProcesses.Contains(process))
                _gameProcesses.Add(process);
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
            PruneExitedProcesses();
            RefreshGameRunningState();
            if (!IsGameRunning && !IsLaunching)
            {
                StatusText = Loc.Get(LocKeys.Home_GameExited);
                ProgressValue = 0;
            }
        });

    private void KillAllTrackedProcesses()
    {
        List<Process> snapshot;
        lock (_processGate)
            snapshot = _gameProcesses.ToList();

        foreach (var process in snapshot)
            TryKillProcess(process);

        if (_gameProcess is not null && !snapshot.Contains(_gameProcess))
            TryKillProcess(_gameProcess);

        PruneExitedProcesses();
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Kill failed: {ex.Message}");
        }
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

                _gameProcesses.RemoveAt(i);
            }
        }
    }

    private void RefreshGameRunningState()
    {
        PruneExitedProcesses();

        var running = false;
        lock (_processGate)
        {
            foreach (var process in _gameProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        running = true;
                        break;
                    }
                }
                catch
                {
                    // treat as dead
                }
            }
        }

        if (IsGameRunning != running)
            IsGameRunning = running;

        UpdateGameWatchTimer(running);
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

        var active = _accounts.GetActive();
        return active is { Kind: AccountKind.Offline } &&
               NameRules.ValidatePlayerName(active.DisplayName) is null;
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

    private bool CanCancel() => IsLaunching;

    partial void OnIsGameRunningChanged(bool value) =>
        StopAllGamesCommand.NotifyCanExecuteChanged();

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
                        if (JavaLocator.GetJavaVersion(currentPath) >= required)
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
        _settings.UseBmclApi = UseBmclApi;
        _settings.JavaPath = JavaPath;
        _settings.SelectedVersion = SelectedVersion?.Id;
        _settings.GameDirectory = GamePaths.GetMinecraftRoot();
        _settings.ForceVersionIsolation = true;
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

    private LauncherSettings SnapshotSettingsWithoutFlush()
    {
        EnsureSettingsLoaded();
        _settings.PlayerName = PlayerName;
        _settings.MaxRamMb = MaxRamMb;
        _settings.UseBmclApi = UseBmclApi;
        _settings.JavaPath = JavaPath;
        _settings.SelectedVersion = SelectedVersion?.Id;
        _settings.GameDirectory = GamePaths.GetMinecraftRoot();
        _settings.ForceVersionIsolation = true;
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

    private async Task WatchProcessAsync(Process process)
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
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Process watch error: {ex}");
            RunOnUi(() => StatusText = Loc.Format(LocKeys.Home_ProcessError, ex.Message));
        }
    }

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
