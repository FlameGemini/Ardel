using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public enum DownloadSection
{
    Minecraft,
    Mod
}

/// <summary>
/// Download page: release / snapshot vanilla clients. Supports concurrent installs.
/// </summary>
public partial class DownloadViewModel : ObservableObject
{
    public const string CategoryIdRelease = "release";
    public const string CategoryIdSnapshot = "snapshot";

    private readonly Lazy<IMinecraftLaunchService> _launchService;
    private readonly LaunchViewModel _launchViewModel;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _filterTimer;
    private readonly object _jobsGate = new();

    /// <summary>Raised on UI thread when a download finishes (success or failure toast).</summary>
    public event EventHandler<DownloadToastEventArgs>? ToastRequested;

    public DownloadViewModel(
        Lazy<IMinecraftLaunchService> launchService,
        LaunchViewModel launchViewModel,
        DispatcherQueue dispatcher)
    {
        _launchService = launchService;
        _launchViewModel = launchViewModel;
        _dispatcher = dispatcher;
        _filterTimer = dispatcher.CreateTimer();
        _filterTimer.IsRepeating = false;
        _filterTimer.Interval = TimeSpan.FromMilliseconds(180);
        _filterTimer.Tick += (_, _) => ApplyFilter();
        CategoryOptions =
        [
            new NamedOption { Id = CategoryIdRelease, Name = Loc.Get(LocKeys.Category_Release) },
            new NamedOption { Id = CategoryIdSnapshot, Name = Loc.Get(LocKeys.Category_Snapshot) }
        ];
        _selectedCategory = CategoryOptions[0];
        _statusText = Loc.Get(LocKeys.Download_SelectHint);
        ActiveDownloads.CollectionChanged += OnActiveDownloadsChanged;
        RefreshGameDirectory();
        ModSearch = new ModSearchViewModel();
    }

    /// <summary>Mod section filter state (results are owned separately).</summary>
    public ModSearchViewModel ModSearch { get; }

    public List<GameVersionItem> AllVersions { get; } = [];

    /// <summary>Bound list — replaced as a whole so ListView gets one update, not N adds.</summary>
    [ObservableProperty] private IReadOnlyList<GameVersionItem> _filteredVersions = Array.Empty<GameVersionItem>();

    public ObservableCollection<DownloadJob> ActiveDownloads { get; } = [];

    public IReadOnlyList<NamedOption> CategoryOptions { get; }

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private NamedOption? _selectedCategory;
    [ObservableProperty] private GameVersionItem? _selectedVersion;
    [ObservableProperty] private bool _hasActiveDownloads;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _gameDirectory = string.Empty;
    [ObservableProperty] private int _activeDownloadCount;
    [ObservableProperty] private bool _isLoadingVersions;
    [ObservableProperty] private DownloadSection _selectedSection = DownloadSection.Minecraft;

    public bool IsMinecraftSection => SelectedSection == DownloadSection.Minecraft;
    public bool IsModSection => SelectedSection == DownloadSection.Mod;

    public bool CanDownloadSelected =>
        SelectedVersion is not null && !IsVersionBusy(SelectedVersion.Id);

    partial void OnSelectedSectionChanged(DownloadSection value)
    {
        OnPropertyChanged(nameof(IsMinecraftSection));
        OnPropertyChanged(nameof(IsModSection));
    }

    /// <summary>Refresh localized labels after <see cref="Loc.SetLanguage"/>.</summary>
    public void Relocalize()
    {
        if (CategoryOptions.Count >= 2)
        {
            CategoryOptions[0].Name = Loc.Get(LocKeys.Category_Release);
            CategoryOptions[1].Name = Loc.Get(LocKeys.Category_Snapshot);
        }

        StatusText = SelectedCategory?.Id == CategoryIdSnapshot
            ? Loc.Get(LocKeys.Download_SelectSnapshot)
            : Loc.Get(LocKeys.Download_SelectRelease);

        ModSearch.Relocalize();
    }

    public void RefreshGameDirectory()
    {
        GameDirectory = GamePaths.GetMinecraftRoot();
    }

    public LauncherSettings SnapshotSettingsForInstall() => _launchViewModel.SnapshotSettings();

    [RelayCommand]
    private void OpenGameDirectory()
    {
        try
        {
            RefreshGameDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = GameDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Download_CannotOpenFolder, ex.Message);
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    partial void OnSelectedCategoryChanged(NamedOption? value)
    {
        ApplyFilter();
        StatusText = value?.Id == CategoryIdSnapshot
            ? Loc.Get(LocKeys.Download_SelectSnapshot)
            : Loc.Get(LocKeys.Download_SelectRelease);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var showSpinner = AllVersions.Count == 0;
        try
        {
            if (showSpinner)
                IsLoadingVersions = true;
            StatusText = Loc.Get(LocKeys.Download_Fetching);

            var settings = _launchViewModel.SnapshotSettings();
            var items = await Task.Run(async () =>
                    await _launchService.Value.GetVersionsAsync(settings).ConfigureAwait(false))
                .ConfigureAwait(true);

            AllVersions.Clear();
            AllVersions.AddRange(items);
            ApplyFilter();
            StatusText = HasActiveDownloads
                ? Loc.Format(LocKeys.Download_AvailableBusy, ActiveDownloadCount, FilteredVersions.Count)
                : Loc.Format(LocKeys.Download_Available, FilteredVersions.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Download_LoadFailed, ex.Message);
            Debug.WriteLine(ex);
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    /// <summary>Enqueue an install from the options dialog.</summary>
    public void StartInstall(InstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetId = request.CustomVersionName.Trim();
        if (IsVersionBusy(targetId) || IsVersionBusy(request.MinecraftVersionId))
        {
            StatusText = Loc.Format(LocKeys.Download_AlreadyRunning, targetId);
            return;
        }

        var preexisting = new HashSet<string>(
            GamePaths.ListVersionFolderNames(GameDirectory),
            StringComparer.OrdinalIgnoreCase);

        var job = new DownloadJob(request, OnJobCancelFromUi)
        {
            WasAlreadyInstalled = GamePaths.IsVersionFullyInstalled(targetId, GameDirectory),
            PreexistingVersionFolders = preexisting
        };

        ActiveDownloads.Insert(0, job);
        NotifyCanDownload();
        StatusText = Loc.Format(LocKeys.Download_Started, targetId, ActiveDownloadCount);

        _ = RunDownloadAsync(job);
    }

    [RelayCommand]
    private void Download()
    {
        // Dialog is shown by DownloadPage (ItemClick / Download button).
    }

    private void NotifyCanDownload()
    {
        OnPropertyChanged(nameof(CanDownloadSelected));
        DownloadCommand.NotifyCanExecuteChanged();
    }

    private void OnJobCancelFromUi(DownloadJob job)
    {
        StatusText = Loc.Format(LocKeys.Download_CancelledNamed, job.VersionId);
        _ = FinishCancelledAsync(job);
    }

    [RelayCommand]
    private void CancelDownload(DownloadJob? job)
    {
        if (job is null || !job.CanCancel)
            return;

        job.Cancel();
        OnJobCancelFromUi(job);
    }

    private async Task RunDownloadAsync(DownloadJob job)
    {
        job.State = DownloadJobState.Running;
        job.IsIndeterminate = true;
        job.StatusText = Loc.Get(LocKeys.Download_Waiting);

        var uiProgress = new CoalescedUiProgress(_dispatcher, (status, progress, indeterminate) =>
        {
            if (!job.IsRunning)
                return;

            // One batched assignment path — avoid three separate PropertyChanged storms.
            job.ApplyProgress(status, progress, indeterminate);
        });

        IProgress<FileProgressInfo> fileProgress = new DirectProgress<FileProgressInfo>(e =>
        {
            if (e.TotalTasks <= 0)
            {
                uiProgress.ReportStatus(
                    string.IsNullOrWhiteSpace(e.Name)
                        ? Loc.Get(LocKeys.Progress_FileFallback)
                        : e.Name);
                return;
            }

            uiProgress.ReportFile(
                string.IsNullOrWhiteSpace(e.Name) ? Loc.Get(LocKeys.Progress_FileFallback) : e.Name,
                e.ProgressedTasks,
                e.TotalTasks);
        });

        var settings = _launchViewModel.SnapshotSettings();

        try
        {
            var installedId = await Task.Run(() => _launchService.Value.InstallAsync(
                    settings,
                    job.Request,
                    fileProgress,
                    byteProgress: null,
                    job.Token))
                .ConfigureAwait(false);

            if (job.IsCancelRequested)
            {
                await FinishCancelledAsync(job).ConfigureAwait(false);
                return;
            }

            RunOnUi(() =>
            {
                job.ProgressValue = 100;
                job.IsIndeterminate = false;
                job.State = DownloadJobState.Completed;
                job.StatusText = Loc.Get(LocKeys.Download_Downloaded);
                RefreshActiveFlags();
                RefreshGameDirectory();
                SetInstalledFlag(job.MinecraftVersionId, installed: true);
                // Must stay on UI thread — ObservableCollection + WinUI bindings throw COMException off-thread.
                _launchViewModel.SelectInstalledVersion(installedId);
                StatusText = Loc.Format(LocKeys.Download_DownloadedNamed, installedId);
                NotifyCanDownload();
                ToastRequested?.Invoke(this, new DownloadToastEventArgs(
                    Loc.Format(LocKeys.Download_CompleteToast, installedId),
                    isError: false));
            });

            // Keep the completed row visible long enough to notice (was 0.9s — easy to miss).
            await Task.Delay(4200).ConfigureAwait(false);
            RunOnUi(() => RemoveJob(job));
        }
        catch (Exception ex) when (IsCancellation(ex, job))
        {
            await FinishCancelledAsync(job).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var message = UnwrapMessage(ex);
            Debug.WriteLine(ex);

            if (job.IsCancelRequested)
            {
                await FinishCancelledAsync(job).ConfigureAwait(false);
                return;
            }

            RunOnUi(() =>
            {
                if (!ActiveDownloads.Contains(job))
                    return;

                job.State = DownloadJobState.Failed;
                job.IsIndeterminate = false;
                job.ProgressValue = 0;
                job.StatusText = message;
                RefreshActiveFlags();
                StatusText = Loc.Format(LocKeys.Download_Failed, job.VersionId, message);
                NotifyCanDownload();
                ToastRequested?.Invoke(this, new DownloadToastEventArgs(
                    Loc.Format(LocKeys.Download_Failed, job.VersionId, message),
                    isError: true));
            });

            await Task.Delay(4200).ConfigureAwait(false);
            RunOnUi(() => RemoveJob(job));
        }
        finally
        {
            job.DisposeToken();
        }
    }

    private async Task FinishCancelledAsync(DownloadJob job)
    {
        if (!job.TryBeginCleanup())
            return;

        job.State = DownloadJobState.Cancelled;
        job.IsIndeterminate = false;
        job.ProgressValue = 0;
        job.StatusText = Loc.Get(LocKeys.Download_Cancelled);

        // Purge the target folder (respect pre-existing complete installs).
        await GamePaths.PurgeCancelledVersionAsync(
                job.VersionId,
                job.WasAlreadyInstalled,
                minecraftRoot: GameDirectory)
            .ConfigureAwait(false);

        // Forge/NeoForge leave default forge-* / neoforge-* folders until rename —
        // remove any folder that did not exist when the job started.
        foreach (var name in GamePaths.ListVersionFolderNames(GameDirectory))
        {
            if (job.PreexistingVersionFolders.Contains(name))
                continue;

            if (string.Equals(name, job.VersionId, StringComparison.OrdinalIgnoreCase))
                continue;

            await GamePaths.PurgeCancelledVersionAsync(
                    name,
                    wasAlreadyInstalled: false,
                    minecraftRoot: GameDirectory)
                .ConfigureAwait(false);
        }

        var vanillaStill = GamePaths.IsVersionFullyInstalled(job.MinecraftVersionId, GameDirectory);

        RunOnUi(() =>
        {
            RemoveJob(job);
            SetInstalledFlag(job.MinecraftVersionId, vanillaStill);
            RefreshActiveFlags();
            StatusText = Loc.Format(LocKeys.Download_CancelledNamed, job.VersionId);
            NotifyCanDownload();
        });

        _launchViewModel.LoadLocalVersions();
    }

    private static bool IsCancellation(Exception ex, DownloadJob job)
    {
        if (job.IsCancelRequested)
            return true;

        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException)
                return true;

            if (e is AggregateException agg &&
                agg.InnerExceptions.Any(inner => inner is OperationCanceledException))
                return true;
        }

        return false;
    }

    private static string UnwrapMessage(Exception ex)
    {
        while (ex is AggregateException { InnerExceptions.Count: 1 } agg)
            ex = agg.InnerExceptions[0];

        while (ex.InnerException is not null &&
               ex is not HttpRequestException and not IOException and not InvalidOperationException)
            ex = ex.InnerException;

        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }

    private void SetInstalledFlag(string versionId, bool installed)
    {
        // Installed badge was removed from the download list — keep data in sync only.
        for (var i = 0; i < AllVersions.Count; i++)
        {
            var item = AllVersions[i];
            if (!string.Equals(item.Id, versionId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (item.IsInstalled == installed)
                return;

            AllVersions[i].IsInstalled = installed;
            return;
        }
    }

    private void RemoveJob(DownloadJob job)
    {
        lock (_jobsGate)
        {
            if (ActiveDownloads.Contains(job))
                ActiveDownloads.Remove(job);
        }

        RefreshActiveFlags();
        NotifyCanDownload();
    }

    private bool IsVersionBusy(string versionId)
    {
        foreach (var job in ActiveDownloads)
        {
            if (!job.IsActive)
                continue;

            if (string.Equals(job.VersionId, versionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.MinecraftVersionId, versionId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void OnActiveDownloadsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshActiveFlags();

    private void RefreshActiveFlags()
    {
        ActiveDownloadCount = ActiveDownloads.Count(j => j.IsActive);
        HasActiveDownloads = ActiveDownloads.Count > 0;
        NotifyCanDownload();
    }

    partial void OnSelectedVersionChanged(GameVersionItem? value) =>
        NotifyCanDownload();

    private void ApplyFilter()
    {
        var categoryId = SelectedCategory?.Id ?? CategoryIdRelease;
        var filter = FilterText.AsSpan().Trim();

        // Build off the UI binding, then swap once.
        var buffer = new List<GameVersionItem>(80);
        foreach (var v in AllVersions)
        {
            if (v.Kind != VersionKind.Vanilla)
                continue;

            if (categoryId == CategoryIdSnapshot)
            {
                if (v.Type != "snapshot")
                    continue;
            }
            else if (v.Type != "release")
            {
                continue;
            }

            if (!filter.IsEmpty &&
                !v.Id.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            buffer.Add(v);
            if (buffer.Count >= 80)
                break;
        }

        FilteredVersions = buffer;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }
}

public sealed class DownloadToastEventArgs : EventArgs
{
    public DownloadToastEventArgs(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }
    public bool IsError { get; }
}
