using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public sealed record ResolutionPreset(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}

public partial class InstanceSettingsViewModel : ObservableObject
{
    private readonly InstanceSettingsStore _store;
    private readonly SettingsService _settingsService;
    private readonly LaunchViewModel _launch;
    private readonly Window _window;
    private string _versionId = string.Empty;
    private string _instanceDirectory = string.Empty;
    private bool _loaded;
    private bool _suppressResolutionSync;
    private XamlRoot? _xamlRoot;
    private CancellationTokenSource? _autoSaveCts;
    private const int AutoSaveDelayMs = 400;

    public InstanceSettingsViewModel(
        InstanceSettingsStore store,
        SettingsService settingsService,
        LaunchViewModel launch,
        Window window)
    {
        _store = store;
        _settingsService = settingsService;
        _launch = launch;
        _window = window;

        ResolutionPresets =
        [
            new ResolutionPreset(Loc.Get(LocKeys.InstanceSettings_ResolutionDefault), 0, 0),
            new ResolutionPreset("1280 × 720", 1280, 720),
            new ResolutionPreset("1600 × 900", 1600, 900),
            new ResolutionPreset("1920 × 1080", 1920, 1080),
            new ResolutionPreset("2560 × 1440", 2560, 1440),
            new ResolutionPreset(Loc.Get(LocKeys.InstanceSettings_ResolutionCustom), -1, -1)
        ];
        _selectedResolutionPreset = ResolutionPresets[0];
    }

    /// <summary>Raised after the instance folder was deleted successfully.</summary>
    public event EventHandler? InstanceDeleted;

    /// <summary>Ask the page to leave settings and show the profiles list (e.g. before launch).</summary>
    public event EventHandler? NavigateToInstancesRequested;

    /// <summary>Open settings for another instance id (after duplicate).</summary>
    public event EventHandler<string>? OpenInstanceRequested;

    public ObservableCollection<JavaInstallation> JavaInstallations => _launch.JavaInstallations;

    public IReadOnlyList<ResolutionPreset> ResolutionPresets { get; }

    [ObservableProperty] private string _versionIdDisplay = string.Empty;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _infoSummary = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _canDelete = true;
    [ObservableProperty] private bool _canRename = true;
    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private bool _overrideJava;
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private string _javaVersionHint = string.Empty;
    [ObservableProperty] private string _kindLabelText = string.Empty;
    [ObservableProperty] private string _baseLabelText = string.Empty;
    [ObservableProperty] private string _suggestedJavaText = string.Empty;
    [ObservableProperty] private bool _isJavaBusy;
    [ObservableProperty] private bool _canEditJava = true;

    [ObservableProperty] private bool _overrideMemory;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private int _minRamMb = 512;
    [ObservableProperty] private int _globalMaxRamMb = 4096;

    [ObservableProperty] private string _extraJvmArguments = string.Empty;
    [ObservableProperty] private string _extraGameArguments = string.Empty;

    [ObservableProperty] private string _screenWidthText = string.Empty;
    [ObservableProperty] private string _screenHeightText = string.Empty;
    [ObservableProperty] private bool _fullScreen;
    [ObservableProperty] private ResolutionPreset _selectedResolutionPreset;

    [ObservableProperty] private string _serverIp = string.Empty;
    [ObservableProperty] private string _serverPortText = string.Empty;

    [ObservableProperty] private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _iconImage;
    [ObservableProperty] private bool _hasCustomIcon;
    [ObservableProperty] private bool _canLaunch = true;
    [ObservableProperty] private bool _canDuplicate = true;

    public void AttachXamlRoot(XamlRoot? root)
    {
        if (root is not null)
            _xamlRoot = root;
    }

    partial void OnIsJavaBusyChanged(bool value) => CanEditJava = !value && OverrideJava;

    partial void OnOverrideJavaChanged(bool value)
    {
        CanEditJava = !IsJavaBusy && value;
        _ = UpdateJavaHintAsync();
        ScheduleAutoSave();
    }

    partial void OnJavaPathChanged(string? value)
    {
        _ = UpdateJavaHintAsync();
        ScheduleAutoSave();
    }

    partial void OnOverrideMemoryChanged(bool value)
    {
        if (!_loaded)
            return;
        if (!value)
        {
            MaxRamMb = GlobalMaxRamMb;
            MinRamMb = Math.Min(512, GlobalMaxRamMb);
        }

        ScheduleAutoSave();
    }

    partial void OnMaxRamMbChanged(int value)
    {
        if (MinRamMb > value)
            MinRamMb = value;
        ScheduleAutoSave();
    }

    partial void OnMinRamMbChanged(int value)
    {
        if (value > MaxRamMb)
            MaxRamMb = value;
        ScheduleAutoSave();
    }

    partial void OnNotesChanged(string value) => ScheduleAutoSave();

    partial void OnExtraJvmArgumentsChanged(string value) => ScheduleAutoSave();

    partial void OnExtraGameArgumentsChanged(string value) => ScheduleAutoSave();

    partial void OnFullScreenChanged(bool value) => ScheduleAutoSave();

    partial void OnServerIpChanged(string value) => ScheduleAutoSave();

    partial void OnServerPortTextChanged(string value) => ScheduleAutoSave();

    partial void OnSelectedResolutionPresetChanged(ResolutionPreset value)
    {
        if (_suppressResolutionSync || value is null)
            return;

        if (value.Width < 0)
            return; // Custom — keep typed values

        _suppressResolutionSync = true;
        ScreenWidthText = value.Width > 0 ? value.Width.ToString() : string.Empty;
        ScreenHeightText = value.Height > 0 ? value.Height.ToString() : string.Empty;
        _suppressResolutionSync = false;
        ScheduleAutoSave();
    }

    partial void OnScreenWidthTextChanged(string value)
    {
        SyncResolutionPresetFromText();
        ScheduleAutoSave();
    }

    partial void OnScreenHeightTextChanged(string value)
    {
        SyncResolutionPresetFromText();
        ScheduleAutoSave();
    }

    public void Load(string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        CancelPendingAutoSave();
        _versionId = versionId.Trim();
        _loaded = false;
        CanDelete = true;
        CanRename = true;
        SelectedTabIndex = 0;

        var global = _settingsService.Load();
        GlobalMaxRamMb = Math.Clamp(global.MaxRamMb, 512, 65536);

        _instanceDirectory = GamePaths.EnsureVersionIsolation(_versionId, global.GameDirectory);
        ApplyIdentity(_versionId, global.GameDirectory);

        var settings = _store.Load(_versionId, global.GameDirectory);
        Notes = settings.Notes ?? string.Empty;
        OverrideJava = settings.OverrideJava;
        JavaPath = settings.JavaPath;
        OverrideMemory = settings.OverrideMemory;
        MaxRamMb = settings.OverrideMemory
            ? Math.Clamp(settings.MaxRamMb, 512, 65536)
            : GlobalMaxRamMb;
        MinRamMb = settings.OverrideMemory && settings.MinRamMb > 0
            ? Math.Clamp(settings.MinRamMb, 512, MaxRamMb)
            : Math.Min(512, MaxRamMb);
        ExtraJvmArguments = settings.ExtraJvmArguments;
        ExtraGameArguments = settings.ExtraGameArguments;
        _suppressResolutionSync = true;
        ScreenWidthText = settings.ScreenWidth > 0 ? settings.ScreenWidth.ToString() : string.Empty;
        ScreenHeightText = settings.ScreenHeight > 0 ? settings.ScreenHeight.ToString() : string.Empty;
        _suppressResolutionSync = false;
        SyncResolutionPresetFromText();
        FullScreen = settings.FullScreen;
        ServerIp = settings.ServerIp;
        ServerPortText = settings.ServerPort > 0 ? settings.ServerPort.ToString() : string.Empty;

        RefreshIconPreview();
        StatusText = string.Empty;
        CanLaunch = true;
        CanDuplicate = true;
        _loaded = true;
        _ = EnsureJavaListAsync();
        _ = UpdateJavaHintAsync();
        _ = ResolveSuggestedJavaAsync(_versionId, global.GameDirectory);
    }

    private void RefreshIconPreview()
    {
        var path = InstanceIconHelper.FindPath(_instanceDirectory);
        IconImage = InstanceIconHelper.CreateImage(path, decodePixels: 96);
        HasCustomIcon = IconImage is not null;
    }

    [RelayCommand]
    private async Task RescanJavaAsync()
    {
        IsJavaBusy = true;
        try
        {
            var javas = await Task.Run(JavaLocator.FindInstallations).ConfigureAwait(true);
            _launch.JavaInstallations.Clear();
            foreach (var java in javas)
                _launch.JavaInstallations.Add(java);

            JavaPath = JavaPath;
            StatusText = Loc.Format(LocKeys.Settings_FoundJava, JavaInstallations.Count);
            await UpdateJavaHintAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_ScanFailed, ex.Message);
            Debug.WriteLine(ex);
        }
        finally
        {
            IsJavaBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseJavaAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            picker.FileTypeFilter.Add(".exe");
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            if (!file.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = Loc.Get(LocKeys.Settings_SelectJavaExe);
                return;
            }

            OverrideJava = true;
            JavaPath = file.Path;
            StatusText = Loc.Get(LocKeys.Settings_JavaUpdated);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_BrowseFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    private void OpenInstanceFolder() => OpenSubfolder(null);

    [RelayCommand]
    private void OpenModsFolder() => OpenSubfolder("mods");

    [RelayCommand]
    private void OpenSavesFolder() => OpenSubfolder("saves");

    [RelayCommand]
    private void OpenConfigFolder() => OpenSubfolder("config");

    [RelayCommand]
    private void OpenResourcepacksFolder() => OpenSubfolder("resourcepacks");

    [RelayCommand]
    private void OpenShaderpacksFolder() => OpenSubfolder("shaderpacks");

    [RelayCommand]
    private void OpenDatapacksFolder() => OpenSubfolder("datapacks");

    [RelayCommand]
    private void OpenScreenshotsFolder() => OpenSubfolder("screenshots");

    [RelayCommand]
    private void OpenLogsFolder() => OpenSubfolder("logs");

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId) || !CanLaunch)
            return;

        FlushPendingSave();

        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, _versionId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            StatusText = Loc.Format(LocKeys.Instances_LoadFailed, _versionId);
            return;
        }

        _launch.SelectedVersion = item;
        NavigateToInstancesRequested?.Invoke(this, EventArgs.Empty);

        if (_launch.LaunchGameCommand.CanExecute(null))
            await _launch.LaunchGameCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId) || !CanDuplicate)
            return;

        FlushPendingSave();
        CanDuplicate = false;
        try
        {
            var global = _settingsService.Load();
            var newId = await Task.Run(() => GamePaths.DuplicateVersion(_versionId, global.GameDirectory))
                .ConfigureAwait(true);

            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            StatusText = Loc.Format(LocKeys.InstanceSettings_Duplicated, newId);
            OpenInstanceRequested?.Invoke(this, newId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusText = Loc.Format(LocKeys.InstanceSettings_DuplicateFailed, ex.Message);
            CanDuplicate = true;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId))
            return;

        FlushPendingSave();
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            picker.SuggestedFileName = _versionId;
            picker.FileTypeChoices.Add("ZIP", [".zip"]);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            StatusText = Loc.Get(LocKeys.InstanceSettings_Exporting);
            var global = _settingsService.Load();
            var path = file.Path;
            await Task.Run(() => GamePaths.ExportVersionZip(_versionId, path, global.GameDirectory))
                .ConfigureAwait(true);
            StatusText = Loc.Format(LocKeys.InstanceSettings_Exported, path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusText = Loc.Format(LocKeys.InstanceSettings_ExportFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task BrowseIconAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId))
            return;

        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var dest = InstanceIconHelper.SetFromFile(_instanceDirectory, file.Path);
            RefreshIconPreview();
            SyncIconToList(dest);
            StatusText = Loc.Get(LocKeys.InstanceSettings_IconUpdated);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusText = Loc.Format(LocKeys.InstanceSettings_IconFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void ClearIcon()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId))
            return;

        InstanceIconHelper.Clear(_instanceDirectory);
        RefreshIconPreview();
        SyncIconToList(null);
        StatusText = Loc.Get(LocKeys.InstanceSettings_IconCleared);
    }

    private void SyncIconToList(string? iconPath)
    {
        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, _versionId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        item.IconPath = iconPath;
        item.RefreshIcon();
    }

    [RelayCommand]
    private void CopyPath()
    {
        try
        {
            Directory.CreateDirectory(_instanceDirectory);
            var package = new DataPackage();
            package.SetText(_instanceDirectory);
            Clipboard.SetContent(package);
            StatusText = Loc.Get(LocKeys.InstanceSettings_PathCopied);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_CannotOpenFolder, ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!_loaded || !CanRename)
            return;

        var newName = (EditName ?? string.Empty).Trim();
        if (string.Equals(newName, _versionId, StringComparison.OrdinalIgnoreCase))
        {
            EditName = _versionId;
            StatusText = string.Empty;
            return;
        }

        var global = _settingsService.Load();
        var versionsRoot = GamePaths.GetVersionsRoot(global.GameDirectory);
        var error = NameRules.ValidateVersionName(newName, versionsRoot, allowExistingId: _versionId);
        if (error is not null)
        {
            StatusText = error;
            return;
        }

        if (!TryBuildSettings(out var settings, out var settingsError))
        {
            StatusText = settingsError;
            return;
        }

        CanRename = false;
        try
        {
            _store.Save(_versionId, settings, global.GameDirectory);

            await Task.Run(() => GamePaths.RenameVersion(_versionId, newName, global.GameDirectory))
                .ConfigureAwait(true);

            if (_launch.SelectedVersion is not null &&
                string.Equals(_launch.SelectedVersion.Id, _versionId, StringComparison.OrdinalIgnoreCase))
            {
                _launch.SelectedVersion = null;
            }

            var snapshot = _launch.SnapshotSettings();
            if (string.Equals(snapshot.SelectedVersion, _versionId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.SelectedVersion = newName;
                _settingsService.Save(snapshot);
            }

            _versionId = newName;
            _instanceDirectory = GamePaths.GetVersionInstanceDirectory(_versionId, global.GameDirectory);
            ApplyIdentity(_versionId, global.GameDirectory);

            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            StatusText = Loc.Format(LocKeys.InstanceSettings_Renamed, _versionId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusText = Loc.Format(LocKeys.InstanceSettings_RenameFailed, ex.Message);
        }
        finally
        {
            CanRename = true;
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        var root = _xamlRoot ?? _window.Content?.XamlRoot;
        if (root is null)
            return;

        var confirm = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get(LocKeys.InstanceSettings_ResetTitle),
            Content = Loc.Get(LocKeys.InstanceSettings_ResetConfirm),
            PrimaryButtonText = Loc.Get(LocKeys.InstanceSettings_Reset),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        Notes = string.Empty;
        OverrideJava = false;
        JavaPath = null;
        OverrideMemory = false;
        MaxRamMb = GlobalMaxRamMb;
        MinRamMb = Math.Min(512, GlobalMaxRamMb);
        ExtraJvmArguments = string.Empty;
        ExtraGameArguments = string.Empty;
        _suppressResolutionSync = true;
        ScreenWidthText = string.Empty;
        ScreenHeightText = string.Empty;
        _suppressResolutionSync = false;
        SelectedResolutionPreset = ResolutionPresets[0];
        FullScreen = false;
        ServerIp = string.Empty;
        ServerPortText = string.Empty;

        Persist(showStatus: false);
        StatusText = Loc.Get(LocKeys.InstanceSettings_ResetDone);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId) || !CanDelete)
            return;

        var root = _xamlRoot ?? _window.Content?.XamlRoot;
        if (root is null)
        {
            StatusText = Loc.Get(LocKeys.Instances_DeleteNoUi);
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get(LocKeys.Instances_DeleteTitle),
            Content = Loc.Format(LocKeys.Instances_DeleteConfirm, _versionId),
            PrimaryButtonText = Loc.Get(LocKeys.Action_Delete),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        CanDelete = false;
        try
        {
            var settings = _settingsService.Load();
            await GamePaths
                .DeleteInstalledVersionAsync(_versionId, settings.GameDirectory)
                .ConfigureAwait(true);

            if (_launch.SelectedVersion is not null &&
                string.Equals(_launch.SelectedVersion.Id, _versionId, StringComparison.OrdinalIgnoreCase))
            {
                _launch.SelectedVersion = null;
            }

            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            InstanceDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusText = Loc.Format(LocKeys.Instances_DeleteFailed, _versionId, ex.Message);
            CanDelete = true;
        }
    }

    /// <summary>Writes any pending edits immediately (e.g. when leaving the page).</summary>
    public void FlushPendingSave()
    {
        CancelPendingAutoSave();
        if (_loaded)
            Persist(showStatus: false);
    }

    private void ScheduleAutoSave()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId))
            return;

        CancelPendingAutoSave();
        var cts = new CancellationTokenSource();
        _autoSaveCts = cts;
        _ = DebouncedPersistAsync(cts.Token);
    }

    private async Task DebouncedPersistAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(AutoSaveDelayMs, token).ConfigureAwait(true);
            Persist(showStatus: true);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer edit
        }
    }

    private void CancelPendingAutoSave()
    {
        try
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _autoSaveCts = null;
    }

    private void Persist(bool showStatus)
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId))
            return;

        try
        {
            if (!TryBuildSettings(out var settings, out var error))
            {
                if (showStatus)
                    StatusText = error;
                return;
            }

            var global = _settingsService.Load();
            _store.Save(_versionId, settings, global.GameDirectory);
            SyncNotesToList(settings.Notes);
            if (showStatus)
                StatusText = Loc.Get(LocKeys.InstanceSettings_Saved);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    private void SyncNotesToList(string notes)
    {
        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, _versionId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            item.Notes = notes;
    }

    private void SyncResolutionPresetFromText()
    {
        if (_suppressResolutionSync)
            return;

        TryParseOptionalPositiveInt(ScreenWidthText, out var width);
        TryParseOptionalPositiveInt(ScreenHeightText, out var height);

        ResolutionPreset match;
        if (width == 0 && height == 0)
            match = ResolutionPresets[0];
        else
            match = ResolutionPresets.FirstOrDefault(p => p.Width == width && p.Height == height)
                    ?? ResolutionPresets.First(p => p.Width < 0);

        if (!ReferenceEquals(SelectedResolutionPreset, match))
        {
            _suppressResolutionSync = true;
            SelectedResolutionPreset = match;
            _suppressResolutionSync = false;
        }
    }

    private void ApplyIdentity(string versionId, string? minecraftRoot)
    {
        VersionIdDisplay = versionId;
        EditName = versionId;

        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        var kindLabel = item?.KindLabel
                        ?? VersionKindDetector.Detect(versionId, minecraftRoot) switch
                        {
                            VersionKind.Fabric => Loc.Get(LocKeys.Install_LoaderFabric),
                            VersionKind.Forge => Loc.Get(LocKeys.Install_LoaderForge),
                            VersionKind.NeoForge => Loc.Get(LocKeys.Install_LoaderNeoForge),
                            VersionKind.OptiFine => Loc.Get(LocKeys.Install_LoaderOptiFine),
                            VersionKind.Custom => Loc.Get(LocKeys.Version_Custom),
                            _ => Loc.Get(LocKeys.Version_Vanilla)
                        };

        var baseId = GamePaths.ResolveBaseGameVersion(versionId, minecraftRoot);
        var baseLabel = string.IsNullOrWhiteSpace(baseId) ||
                        string.Equals(baseId, versionId, StringComparison.OrdinalIgnoreCase)
            ? null
            : baseId;

        var javaLabel = item?.OfficialJavaMajor is int major and > 0
            ? Loc.Format(LocKeys.Settings_JavaSelected, major)
            : null;

        InfoSummary = string.Join(
            " · ",
            new[] { kindLabel, baseLabel, javaLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));

        KindLabelText = kindLabel;
        BaseLabelText = baseLabel ?? versionId;
        SuggestedJavaText = javaLabel ?? Loc.Get(LocKeys.InstanceSettings_InfoJavaUnknown);
    }

    private async Task ResolveSuggestedJavaAsync(string versionId, string? minecraftRoot)
    {
        var major = await Task.Run(() => OfficialJavaRequirements.TryReadLocal(versionId, minecraftRoot)).ConfigureAwait(true);
        if (major is int resolvedMajor and > 0)
        {
            var item = _launch.Versions.FirstOrDefault(v =>
                string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.OfficialJavaMajor = resolvedMajor;
            }

            var javaLabel = Loc.Format(LocKeys.Settings_JavaSelected, resolvedMajor);
            SuggestedJavaText = javaLabel;

            var kindLabel = KindLabelText;
            var baseLabel = string.Equals(BaseLabelText, versionId, StringComparison.OrdinalIgnoreCase) ? null : BaseLabelText;
            InfoSummary = string.Join(
                " · ",
                new[] { kindLabel, baseLabel, javaLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    private void OpenSubfolder(string? relative)
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(relative)
                ? _instanceDirectory
                : Path.Combine(_instanceDirectory, relative);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_CannotOpenFolder, ex.Message);
        }
    }

    private bool TryBuildSettings(out InstanceSettings settings, out string error)
    {
        var max = Math.Clamp(MaxRamMb, 512, 65536);
        var min = Math.Clamp(MinRamMb, 512, max);

        settings = new InstanceSettings
        {
            Notes = Notes?.Trim() ?? string.Empty,
            OverrideJava = OverrideJava,
            JavaPath = JavaPath,
            OverrideMemory = OverrideMemory,
            MaxRamMb = max,
            MinRamMb = OverrideMemory ? min : 0,
            ExtraJvmArguments = ExtraJvmArguments ?? string.Empty,
            ExtraGameArguments = ExtraGameArguments ?? string.Empty,
            FullScreen = FullScreen,
            ServerIp = ServerIp?.Trim() ?? string.Empty
        };
        error = string.Empty;

        if (!TryParseOptionalPositiveInt(ScreenWidthText, out var width))
        {
            error = Loc.Get(LocKeys.InstanceSettings_InvalidResolution);
            return false;
        }

        if (!TryParseOptionalPositiveInt(ScreenHeightText, out var height))
        {
            error = Loc.Get(LocKeys.InstanceSettings_InvalidResolution);
            return false;
        }

        settings.ScreenWidth = width;
        settings.ScreenHeight = height;

        if (!TryParseOptionalPort(ServerPortText, out var port))
        {
            error = Loc.Get(LocKeys.InstanceSettings_InvalidPort);
            return false;
        }

        settings.ServerPort = port;

        if (settings.OverrideJava &&
            !string.IsNullOrWhiteSpace(settings.JavaPath) &&
            !File.Exists(settings.JavaPath))
        {
            error = Loc.Get(LocKeys.InstanceSettings_JavaMissing);
            return false;
        }

        return true;
    }

    private static bool TryParseOptionalPositiveInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!int.TryParse(text.Trim(), out value) || value <= 0)
            return false;
        return true;
    }

    private static bool TryParseOptionalPort(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!int.TryParse(text.Trim(), out value) || value is < 1 or > 65535)
            return false;
        return true;
    }

    private async Task EnsureJavaListAsync()
    {
        if (JavaInstallations.Count > 0)
            return;
        await RescanJavaAsync().ConfigureAwait(true);
    }

    private async Task UpdateJavaHintAsync()
    {
        string? path = null;
        bool isGlobal = !OverrideJava;

        if (isGlobal)
        {
            var global = _settingsService.Load();
            path = global.JavaPath;
        }
        else
        {
            path = JavaPath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            string autoText = Loc.Get(LocKeys.Settings_JavaAuto);
            JavaVersionHint = isGlobal
                ? $"{Loc.Get(LocKeys.InstanceSettings_JavaFollowGlobal)} ({autoText})"
                : autoText;
            return;
        }

        try
        {
            var major = await Task.Run(() => JavaLocator.GetJavaVersion(path)).ConfigureAwait(true);
            string versionStr = Loc.Format(LocKeys.Settings_JavaSelected, major);
            JavaVersionHint = isGlobal
                ? $"{Loc.Get(LocKeys.InstanceSettings_JavaFollowGlobal)} ({versionStr})"
                : versionStr;
        }
        catch (Exception ex)
        {
            JavaVersionHint = ex.Message;
        }
    }

    [RelayCommand]
    private void ImportJvmPreset(string presetType)
    {
        if (presetType == "clean")
        {
            ExtraJvmArguments = string.Empty;
            return;
        }

        string preset = presetType switch
        {
            "g1gc" => "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M",
            "shenandoah" => "-XX:+UseShenandoahGC -XX:+UnlockExperimentalVMOptions -XX:ShenandoahGCHeuristics=adaptive",
            "zgc" => "-XX:+UseZGC -XX:+UnlockExperimentalVMOptions -XX:ZGCAllocationSpikeTolerance=5",
            "genzgc" => "-XX:+UseZGC -XX:+ZGenerational -XX:+UnlockExperimentalVMOptions -XX:ZGCAllocationSpikeTolerance=5",
            "graalvm" => "-XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch -XX:+UseNUMA",
            "lowlatency" => "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:MaxGCPauseMillis=15 -XX:G1ReservePercent=15 -XX:G1NewSizePercent=30 -XX:G1HeapRegionSize=16M -XX:+ParallelRefProcEnabled",
            "lowpc" => "-XX:+UseSerialGC -XX:MinHeapFreeRatio=10 -XX:MaxHeapFreeRatio=20",
            "aikar" => "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(preset))
            return;

        var newList = string.IsNullOrWhiteSpace(ExtraJvmArguments)
            ? new List<string>()
            : new List<string>(ExtraJvmArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        bool hasGc = preset.Contains("-XX:+UseG1GC") || preset.Contains("-XX:+UseZGC") ||
                     preset.Contains("-XX:+UseShenandoahGC") || preset.Contains("-XX:+UseSerialGC");

        if (hasGc)
        {
            newList.RemoveAll(x => x.StartsWith("-XX:+UseG1GC") || x.StartsWith("-XX:+UseZGC") ||
                                   x.StartsWith("-XX:+UseShenandoahGC") || x.StartsWith("-XX:+UseSerialGC") ||
                                   x.StartsWith("-XX:+UseParallelGC") || x.StartsWith("-XX:+UseConcMarkSweepGC"));

            if (!preset.Contains("-XX:+UseZGC"))
            {
                newList.RemoveAll(x => x.StartsWith("-XX:+ZGenerational") || x.StartsWith("-XX:ZGCAllocationSpikeTolerance"));
            }

            if (!preset.Contains("-XX:+UseG1GC"))
            {
                newList.RemoveAll(x => x.StartsWith("-XX:G1NewSizePercent") || 
                                       x.StartsWith("-XX:G1ReservePercent") || 
                                       x.StartsWith("-XX:G1HeapRegionSize") ||
                                       x.StartsWith("-XX:G1MaxNewSizePercent") ||
                                       x.StartsWith("-XX:G1HeapWastePercent") ||
                                       x.StartsWith("-XX:G1MixedGCCountTarget") ||
                                       x.StartsWith("-XX:G1MixedGCLiveThresholdPercent") ||
                                       x.StartsWith("-XX:G1RSetUpdatingPauseTimePercent"));
            }

            if (!preset.Contains("-XX:+UseShenandoahGC"))
            {
                newList.RemoveAll(x => x.StartsWith("-XX:ShenandoahGCHeuristics"));
            }

            if (!preset.Contains("-XX:+UseSerialGC"))
            {
                newList.RemoveAll(x => x.StartsWith("-XX:MinHeapFreeRatio") || x.StartsWith("-XX:MaxHeapFreeRatio"));
            }
        }

        var toAdd = preset.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var flag in toAdd)
        {
            if (!newList.Contains(flag))
            {
                newList.Add(flag);
            }
        }

        ExtraJvmArguments = string.Join(' ', newList);
    }
}
