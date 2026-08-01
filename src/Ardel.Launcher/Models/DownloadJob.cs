using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Models;

public enum DownloadJobKind
{
    VersionInstall,
    ModFile,
    Modpack
}

/// <summary>
/// One in-flight (or recently finished) install / Mod download task.
/// </summary>
public partial class DownloadJob : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<DownloadJob>? _onCancel;
    private int _cancelRequested;
    private int _cleanupStarted;

    public DownloadJob(InstallRequest request, Action<DownloadJob>? onCancel = null)
    {
        Kind = DownloadJobKind.VersionInstall;
        VersionRequest = request;
        ModRequest = null;
        ModpackRequest = null;
        VersionId = request.CustomVersionName;
        MinecraftVersionId = request.MinecraftVersionId;
        _onCancel = onCancel;
        StatusText = Loc.Get(LocKeys.Download_Queued);
        IsIndeterminate = true;
        CancelCommand = new RelayCommand(
            () =>
            {
                Cancel();
                _onCancel?.Invoke(this);
            },
            () => CanCancel);
    }

    public DownloadJob(ModFileInstallRequest request, Action<DownloadJob>? onCancel = null)
    {
        Kind = DownloadJobKind.ModFile;
        VersionRequest = null;
        ModRequest = request;
        ModpackRequest = null;
        VersionId = request.DisplayName;
        MinecraftVersionId = request.TargetInstanceId;
        _onCancel = onCancel;
        StatusText = Loc.Get(LocKeys.Download_Queued);
        IsIndeterminate = true;
        CancelCommand = new RelayCommand(
            () =>
            {
                Cancel();
                _onCancel?.Invoke(this);
            },
            () => CanCancel);
    }

    public DownloadJob(ModpackInstallRequest request, Action<DownloadJob>? onCancel = null)
    {
        Kind = DownloadJobKind.Modpack;
        VersionRequest = null;
        ModRequest = null;
        ModpackRequest = request;
        VersionId = request.InstanceName;
        MinecraftVersionId = request.InstanceName;
        _onCancel = onCancel;
        StatusText = Loc.Get(LocKeys.Download_Queued);
        IsIndeterminate = true;
        CancelCommand = new RelayCommand(
            () =>
            {
                Cancel();
                _onCancel?.Invoke(this);
            },
            () => CanCancel);
    }

    public DownloadJobKind Kind { get; }

    public InstallRequest? VersionRequest { get; }

    public ModFileInstallRequest? ModRequest { get; }

    public ModpackInstallRequest? ModpackRequest { get; }

    /// <summary>Legacy alias for version installs.</summary>
    public InstallRequest Request =>
        VersionRequest ?? throw new InvalidOperationException("Not a version install job.");

    /// <summary>Target / display name in the task flyout.</summary>
    public string VersionId { get; }

    public string MinecraftVersionId { get; }

    public IRelayCommand CancelCommand { get; }

    public CancellationToken Token => _cts.Token;

    public bool IsCancelRequested => Volatile.Read(ref _cancelRequested) != 0;

    public bool IsRunning => State == DownloadJobState.Running;

    public bool IsActive => State is DownloadJobState.Queued or DownloadJobState.Running or DownloadJobState.Cancelling;

    public bool CanCancel => State is DownloadJobState.Queued or DownloadJobState.Running;

    public bool WasAlreadyInstalled { get; set; }

    /// <summary>
    /// Version folder names present before this job started.
    /// Used on cancel to purge Forge/NeoForge intermediates left behind.
    /// </summary>
    public IReadOnlySet<string> PreexistingVersionFolders { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns true once — used so cancel UI and install finally do not double-purge.</summary>
    public bool TryBeginCleanup() => Interlocked.CompareExchange(ref _cleanupStarted, 1, 0) == 0;

    [ObservableProperty] private DownloadJobState _state = DownloadJobState.Queued;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;

    partial void OnStateChanged(DownloadJobState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanCancel));
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void ApplyProgress(string status, double progress, bool indeterminate)
    {
        var statusChanged = !string.IsNullOrEmpty(status) && !string.Equals(StatusText, status, StringComparison.Ordinal);
        var progressChanged = Math.Abs(ProgressValue - progress) >= 0.25;
        var indeterminateChanged = IsIndeterminate != indeterminate;

        if (!statusChanged && !progressChanged && !indeterminateChanged)
            return;

        if (statusChanged)
            StatusText = status;
        if (progressChanged)
            ProgressValue = progress;
        if (indeterminateChanged)
            IsIndeterminate = indeterminate;
    }

    public void Cancel()
    {
        if (!CanCancel && !IsCancelRequested)
            return;

        if (CanCancel)
        {
            Volatile.Write(ref _cancelRequested, 1);
            State = DownloadJobState.Cancelling;
            IsIndeterminate = false;
            ProgressValue = 0;
            StatusText = Loc.Get(LocKeys.Download_Cancelling);

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void DisposeToken()
    {
        try
        {
            _cts.Dispose();
        }
        catch
        {
        }
    }
}

public enum DownloadJobState
{
    Queued,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}
