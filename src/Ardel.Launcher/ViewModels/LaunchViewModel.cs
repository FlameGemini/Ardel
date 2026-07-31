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
    private readonly DispatcherQueue _dispatcher;
    private LauncherSettings _settings = new();
    private CancellationTokenSource? _launchCts;
    private Process? _gameProcess;
    private bool _suppressPersist = true;
    private bool _localReady;
    private bool _settingsLoaded;

    public LaunchViewModel(
        Lazy<IMinecraftLaunchService> launchService,
        LocalVersionStore localVersions,
        SettingsService settingsService,
        DispatcherQueue dispatcher)
    {
        _launchService = launchService;
        _localVersions = localVersions;
        _settingsService = settingsService;
        _dispatcher = dispatcher;
        // No disk I/O / CmlLib in ctor — first paint stays light
    }

    public ObservableCollection<GameVersionItem> Versions { get; } = [];
    public ObservableCollection<JavaInstallation> JavaInstallations { get; } = [];

    [ObservableProperty] private GameVersionItem? _selectedVersion;
    [ObservableProperty] private string _playerName = Loc.Get(LocKeys.Default_PlayerName);
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private bool _useBmclApi;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _avatarInitials = "P";

    partial void OnPlayerNameChanged(string value)
    {
        AvatarInitials = string.IsNullOrWhiteSpace(value)
            ? "?"
            : value.Trim()[..1].ToUpperInvariant();
        Persist();
    }

    partial void OnSelectedVersionChanged(GameVersionItem? value)
    {
        if (value is not null)
            EnsureSuitableJavaSelected(value.Id);

        Persist();
    }
    partial void OnMaxRamMbChanged(int value) => Persist();
    partial void OnUseBmclApiChanged(bool value) => Persist();
    partial void OnJavaPathChanged(string? value) => Persist();

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
        PlayerName = _settings.PlayerName;
        MaxRamMb = _settings.MaxRamMb;
        UseBmclApi = _settings.UseBmclApi;
        JavaPath = _settings.JavaPath;
        AvatarInitials = string.IsNullOrWhiteSpace(PlayerName)
            ? "?"
            : PlayerName.Trim()[..1].ToUpperInvariant();
        _suppressPersist = false;
        _settingsLoaded = true;
    }

    public void LoadLocalVersions()
    {
        EnsureSettingsLoaded();
        var gameDir = SnapshotSettings().GameDirectory;
        var items = _localVersions.GetInstalled(gameDir);
        var saved = _settings.SelectedVersion;

        Versions.Clear();
        foreach (var item in items)
            Versions.Add(item);

        _suppressPersist = true;
        SelectedVersion = Versions.FirstOrDefault(v =>
                              string.Equals(v.Id, saved, StringComparison.OrdinalIgnoreCase))
                          ?? Versions.FirstOrDefault();
        _suppressPersist = false;
    }

    /// <summary>Reload local instances and select the version that was just installed.</summary>
    public void SelectInstalledVersion(string versionId)
    {
        EnsureSettingsLoaded();
        var gameDir = SnapshotSettings().GameDirectory;
        var items = _localVersions.GetInstalled(gameDir);

        Versions.Clear();
        foreach (var item in items)
            Versions.Add(item);

        _suppressPersist = true;
        SelectedVersion = Versions.FirstOrDefault(v =>
                              string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase))
                          ?? Versions.FirstOrDefault();
        _suppressPersist = false;
        Persist();
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchGameAsync()
    {
        if (SelectedVersion is null || IsLaunching)
            return;

        var nameError = NameRules.ValidatePlayerName(PlayerName);
        if (nameError is not null)
        {
            StatusText = nameError;
            return;
        }

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

            _gameProcess = await Task.Run(
                    () => _launchService.Value.LaunchAsync(
                        settings,
                        SelectedVersion.Id,
                        PlayerName,
                        fileProgress,
                        byteProgress,
                        token),
                    token)
                .ConfigureAwait(true);

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
    }

    private bool CanLaunch() => !IsLaunching && SelectedVersion is not null;
    private bool CanCancel() => IsLaunching;

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
            EnsureSuitableJavaSelected(SelectedVersion.Id);
    }

    /// <summary>
    /// Prefer a local Java that meets the required major. Never keep / pick a too-old install.
    /// Empty path means launch will download a matching Temurin runtime.
    /// Uses official <c>javaVersion.majorVersion</c> when already cached/local; otherwise waits for launch.
    /// </summary>
    private void EnsureSuitableJavaSelected(string? versionId = null, int? requiredMajor = null)
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
                return;
        }
        else
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(JavaPath) && File.Exists(JavaPath))
        {
            try
            {
                if (JavaLocator.GetJavaVersion(JavaPath) >= required)
                    return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LaunchViewModel] Java probe failed: {ex.Message}");
            }
        }

        var best = JavaLocator.FindBestMatch(required, JavaInstallations)?.JavaExePath
                   ?? JavaRuntimeInstaller.TryFindInstalled(required);

        _suppressPersist = true;
        JavaPath = best;
        _suppressPersist = false;
    }

    public LauncherSettings SnapshotSettings()
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

    private void Persist(LauncherSettings? settings = null)
    {
        if (_suppressPersist || !_settingsLoaded)
            return;

        try
        {
            _settingsService.Save(settings ?? SnapshotSettings());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LaunchViewModel] Persist failed: {ex}");
        }
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
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                RunOnUi(() =>
                {
                    StatusText = Loc.Get(LocKeys.Home_GameExited);
                    ProgressValue = 0;
                });
            };

            await process.WaitForExitAsync().ConfigureAwait(false);
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
