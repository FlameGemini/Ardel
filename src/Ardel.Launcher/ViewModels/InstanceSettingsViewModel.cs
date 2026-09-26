using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly InstanceStatsStore _statsStore;
    private readonly Window _window;
    private string _versionId = string.Empty;
    private string _instanceDirectory = string.Empty;
    private bool _loaded;
    private bool _suppressResolutionSync;
    private XamlRoot? _xamlRoot;
    private CancellationTokenSource? _autoSaveCts;
    private CancellationTokenSource? _resourceLoadCts;
    private const int AutoSaveDelayMs = 400;
    private readonly UiCoalesce _modSelectionCoalesce;
    private DispatcherQueue? _dispatcher;
    private FileSystemWatcher? _resourceWatcher;
    private string? _resourceWatcherRoot;
    private DispatcherQueueTimer? _resourceReloadTimer;

    public string InstanceDirectory => _instanceDirectory;
    public static InstanceSettingsViewModel? ActiveInstance { get; private set; }

    public InstanceSettingsViewModel(
        InstanceSettingsStore store,
        SettingsService settingsService,
        LaunchViewModel launch,
        InstanceStatsStore statsStore,
        Window window)
    {
        _store = store;
        _settingsService = settingsService;
        _launch = launch;
        _statsStore = statsStore;
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
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _modSelectionCoalesce = new UiCoalesce(
            _dispatcher ?? DispatcherQueue.GetForCurrentThread(),
            RefreshModSelectionStateCore);

        var dispatcher = _dispatcher ?? DispatcherQueue.GetForCurrentThread();
        ResourcePacks = CreatePackManager("resourcepacks", CatalogProjectKind.ResourcePack, DownloadSection.ResourcePack, dispatcher);
        ShaderPacks = CreatePackManager("shaderpacks", CatalogProjectKind.ShaderPack, DownloadSection.ShaderPack, dispatcher);

        _resourceReloadTimer = dispatcher.CreateTimer();
        _resourceReloadTimer.IsRepeating = false;
        _resourceReloadTimer.Interval = TimeSpan.FromMilliseconds(500);
        _resourceReloadTimer.Tick += (_, _) =>
        {
            if (_loaded)
                LoadResourceLists();
        };

        _launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchViewModel.IsLaunching))
                SyncCanLaunchFromLaunchState();
        };
        _launch.GameExited += (_, _) =>
        {
            if (SelectedTabIndex == 7)
                _ = LoadStatisticsAsync();
        };
    }

    private void SyncCanLaunchFromLaunchState()
    {
        if (!_loaded)
            return;
        CanLaunch = !_launch.IsLaunching;
    }

    private InstancePackManager CreatePackManager(
        string folderName,
        CatalogProjectKind kind,
        DownloadSection section,
        DispatcherQueue dispatcher) =>
        new(
            folderName,
            kind,
            section,
            () => _instanceDirectory,
            () => _versionId,
            LoadResourceLists,
            dispatcher);

    /// <summary>Raised after the instance folder was deleted successfully.</summary>
    public event EventHandler? InstanceDeleted;

    /// <summary>Ask the page to leave settings and show the profiles list (e.g. before launch).</summary>
    public event EventHandler? NavigateToInstancesRequested;

    /// <summary>Open settings for another instance id (after duplicate).</summary>
    public event EventHandler<string>? OpenInstanceRequested;

    public ObservableCollection<JavaInstallation> JavaInstallations => _launch.JavaInstallations;

    public IReadOnlyList<ResolutionPreset> ResolutionPresets { get; }

    [ObservableProperty] private string _versionIdDisplay = string.Empty;

    public string VersionId => _versionId;
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

    [ObservableProperty] private int _memoryMode;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private int _globalMaxRamMb = 4096;
    [ObservableProperty] private int _globalMemoryMode;
    [ObservableProperty] private int _systemTotalRamMb = 16384;
    [ObservableProperty] private int _memorySliderMaximum = 16384;
    [ObservableProperty] private bool _showCustomMemoryControls;
    [ObservableProperty] private bool _showFollowMemoryHint;
    [ObservableProperty] private string _followMemoryHintText = string.Empty;
    [ObservableProperty] private string _memoryUsedText = string.Empty;
    [ObservableProperty] private string _memoryTotalText = string.Empty;
    [ObservableProperty] private string _memoryGameText = string.Empty;
    [ObservableProperty] private string _memoryFreeText = string.Empty;
    [ObservableProperty] private GridLength _memoryUsedColumn = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _memoryGameColumn = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _memoryEmptyColumn = new(1, GridUnitType.Star);

    /// <summary>Radio bindings — only react when becoming checked.</summary>
    public bool IsMemoryFollowDefault
    {
        get => MemoryMode == (int)InstanceMemoryMode.FollowDefault;
        set
        {
            if (value)
                MemoryMode = (int)InstanceMemoryMode.FollowDefault;
        }
    }

    public bool IsMemoryCustom
    {
        get => MemoryMode == (int)InstanceMemoryMode.Custom;
        set
        {
            if (value)
                MemoryMode = (int)InstanceMemoryMode.Custom;
        }
    }

    public bool IsMemoryDynamic
    {
        get => MemoryMode == (int)InstanceMemoryMode.Dynamic;
        set
        {
            if (value)
                MemoryMode = (int)InstanceMemoryMode.Dynamic;
        }
    }

    [ObservableProperty] private string _extraJvmArguments = string.Empty;
    [ObservableProperty] private string _extraGameArguments = string.Empty;

    [ObservableProperty] private string _screenWidthText = string.Empty;
    [ObservableProperty] private string _screenHeightText = string.Empty;
    [ObservableProperty] private bool _fullScreen;
    [ObservableProperty] private string _windowTitle = string.Empty;
    [ObservableProperty] private ResolutionPreset _selectedResolutionPreset;

    [ObservableProperty] private string _serverIp = string.Empty;
    [ObservableProperty] private string _serverPortText = string.Empty;

    [ObservableProperty] private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _iconImage;
    [ObservableProperty] private bool _hasCustomIcon;
    [ObservableProperty] private string _iconGlyph = "\uE7FC";
    [ObservableProperty] private bool _canLaunch = true;
    [ObservableProperty] private bool _canDuplicate = true;
    [ObservableProperty] private bool _pinToQuickLaunch;

    public string[] PresetGlyphs { get; } = [
        "\uE7FC", // Cube
        "\uE704", // Globe
        "\uE7C8", // Shield
        "\uE7E7", // Lightning
        "\uEC1B", // Crown
        "\uE70A", // Star
        "\uE8A5", // Heart
        "\uE91C", // Puzzle
        "\uED55", // Controller
        "\uE724", // Compass
        "\uE7B5", // Wrench
        "\uE734", // Trophy
        "\uE80F", // Home
        "\uEB51", // Cloud
        "\uE909", // Fire
        "\uEA18"  // Hourglass
    ];

    public void AttachXamlRoot(XamlRoot? root)
    {
        if (root is not null)
            _xamlRoot = root;
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
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

    partial void OnMemoryModeChanged(int value)
    {
        ShowCustomMemoryControls = value == (int)InstanceMemoryMode.Custom;
        ShowFollowMemoryHint = value == (int)InstanceMemoryMode.FollowDefault;
        FollowMemoryHintText = GlobalMemoryMode == (int)DefaultMemoryMode.Dynamic
            ? Loc.Get(LocKeys.Memory_FollowDefaultDynamic)
            : Loc.Format(LocKeys.Memory_FollowDefaultCustom, GlobalMaxRamMb);
        OnPropertyChanged(nameof(IsMemoryFollowDefault));
        OnPropertyChanged(nameof(IsMemoryCustom));
        OnPropertyChanged(nameof(IsMemoryDynamic));
        if (!_loaded)
            return;
        RefreshMemoryPreview();
        ScheduleAutoSave();
    }

    partial void OnMaxRamMbChanged(int value)
    {
        RefreshMemoryPreview();
        if (!_loaded)
            return;
        ScheduleAutoSave();
    }

    partial void OnNotesChanged(string value) => ScheduleAutoSave();

    partial void OnPinToQuickLaunchChanged(bool value)
    {
        if (!_loaded)
            return;
        PersistQuickLaunchPin(value);
    }

    private void PersistQuickLaunchPin(bool pinned)
    {
        try
        {
            var snapshot = _launch.SnapshotSettings();
            if (pinned)
            {
                snapshot.QuickLaunchVersionId = _versionId;
            }
            else if (string.Equals(
                         snapshot.QuickLaunchVersionId,
                         _versionId,
                         StringComparison.OrdinalIgnoreCase))
            {
                snapshot.QuickLaunchVersionId = null;
            }

            _settingsService.Save(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettings] Quick launch pin failed: {ex}");
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
        }
    }

    partial void OnExtraJvmArgumentsChanged(string value) => ScheduleAutoSave();

    partial void OnExtraGameArgumentsChanged(string value) => ScheduleAutoSave();

    partial void OnFullScreenChanged(bool value) => ScheduleAutoSave();

    partial void OnWindowTitleChanged(string value) => ScheduleAutoSave();

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
        ActiveInstance = this;
        CancelPendingAutoSave();
        DisposeResourceWatchers();
        _versionId = versionId.Trim();
        _loaded = false;
        CanDelete = true;
        CanRename = true;
        SelectedTabIndex = 0;
        CloseSaveProperties();
        StatusText = string.Empty;
        CanLaunch = !_launch.IsLaunching;
        CanDuplicate = true;

        // Chrome only — no settings disk. First paint must not wait on Load().
        ApplyIdentityFromMemory(_versionId);
    }

    /// <summary>Disk + full bindings after the page has had a chance to paint.</summary>
    public async Task CompleteLoadAfterPaintAsync(string versionId)
    {
        var id = versionId.Trim();
        if (!string.Equals(_versionId, id, StringComparison.Ordinal))
            return;

        // Slow path off the UI thread — File.ReadAllText / version JSON probes.
        var snapshot = await Task.Run(() =>
        {
            var global = _settingsService.Load();
            var settings = _store.Load(id, global.GameDirectory);
            var instanceDirectory = GamePaths.GetVersionInstanceDirectory(id, global.GameDirectory);
            var kind = VersionKindDetector.Detect(id, global.GameDirectory);
            var baseId = GamePaths.ResolveBaseGameVersion(id, global.GameDirectory);
            return (global, settings, instanceDirectory, kind, baseId);
        }).ConfigureAwait(true);

        if (!string.Equals(_versionId, id, StringComparison.Ordinal))
            return;

        var global = snapshot.global;
        GlobalMaxRamMb = Math.Clamp(global.MaxRamMb, 512, 65536);
        GlobalMemoryMode = MemoryLaunchResolver.NormalizeDefaultMode(global) == DefaultMemoryMode.Dynamic
            ? (int)DefaultMemoryMode.Dynamic
            : (int)DefaultMemoryMode.Custom;
        SystemTotalRamMb = SystemMemory.TotalMegabytes;
        MemorySliderMaximum = Math.Max(2048, Math.Min(65536, SystemTotalRamMb));

        _instanceDirectory = snapshot.instanceDirectory;
        ApplyIdentityResolved(id, snapshot.kind, snapshot.baseId);

        var settings = snapshot.settings;
        Notes = settings.Notes ?? string.Empty;
        IconGlyph = settings.IconGlyph ?? "\uE7FC";
        PinToQuickLaunch = !string.IsNullOrWhiteSpace(global.QuickLaunchVersionId) &&
            string.Equals(global.QuickLaunchVersionId.Trim(), _versionId, StringComparison.OrdinalIgnoreCase);
        OverrideJava = settings.OverrideJava;
        JavaPath = settings.JavaPath;
        MemoryMode = (int)MemoryLaunchResolver.NormalizeInstanceMode(settings);
        MaxRamMb = settings.MaxRamMb > 0
            ? Math.Clamp(settings.MaxRamMb, 512, MemorySliderMaximum)
            : Math.Clamp(GlobalMaxRamMb, 512, MemorySliderMaximum);
        ShowCustomMemoryControls = MemoryMode == (int)InstanceMemoryMode.Custom;
        ShowFollowMemoryHint = MemoryMode == (int)InstanceMemoryMode.FollowDefault;
        FollowMemoryHintText = GlobalMemoryMode == (int)DefaultMemoryMode.Dynamic
            ? Loc.Get(LocKeys.Memory_FollowDefaultDynamic)
            : Loc.Format(LocKeys.Memory_FollowDefaultCustom, GlobalMaxRamMb);
        OnPropertyChanged(nameof(IsMemoryFollowDefault));
        OnPropertyChanged(nameof(IsMemoryCustom));
        OnPropertyChanged(nameof(IsMemoryDynamic));
        ExtraJvmArguments = settings.ExtraJvmArguments;
        ExtraGameArguments = settings.ExtraGameArguments;
        _suppressResolutionSync = true;
        ScreenWidthText = settings.ScreenWidth > 0 ? settings.ScreenWidth.ToString() : string.Empty;
        ScreenHeightText = settings.ScreenHeight > 0 ? settings.ScreenHeight.ToString() : string.Empty;
        _suppressResolutionSync = false;
        SyncResolutionPresetFromText();
        FullScreen = settings.FullScreen;
        WindowTitle = settings.WindowTitle ?? string.Empty;
        ServerIp = settings.ServerIp;
        ServerPortText = settings.ServerPort > 0 ? settings.ServerPort.ToString() : string.Empty;

        RefreshMemoryPreview();
        StatusText = string.Empty;
        CanLaunch = !_launch.IsLaunching;
        CanDuplicate = true;
        _loaded = true;

        var capturedId = _versionId;
        var capturedDir = global.GameDirectory;
        var dispatcher = _dispatcher ?? DispatcherQueue.GetForCurrentThread();
        dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!string.Equals(_versionId, capturedId, StringComparison.Ordinal))
                return;
            try
            {
                GamePaths.EnsureVersionIsolation(capturedId, capturedDir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettingsViewModel] EnsureVersionIsolation: {ex.Message}");
            }
            RefreshIconPreview();
            EnsureResourceWatchers();
            LoadResourceLists();
            _ = EnsureJavaListAsync();
            _ = UpdateJavaHintAsync();
            _ = ResolveSuggestedJavaAsync(capturedId, capturedDir);
        });
    }

    private void ApplyIdentityFromMemory(string versionId)
    {
        VersionIdDisplay = versionId;
        EditName = versionId;

        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            KindLabelText = string.Empty;
            BaseLabelText = versionId;
            SuggestedJavaText = Loc.Get(LocKeys.InstanceSettings_InfoJavaUnknown);
            InfoSummary = versionId;
            return;
        }

        KindLabelText = item.KindLabel;
        BaseLabelText = item.Id;
        var javaLabel = item.OfficialJavaMajor is int major and > 0
            ? Loc.Format(LocKeys.Settings_JavaSelected, major)
            : null;
        SuggestedJavaText = javaLabel ?? Loc.Get(LocKeys.InstanceSettings_InfoJavaUnknown);
        InfoSummary = string.Join(
            " · ",
            new[] { item.KindLabel, javaLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));
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
    private void RefreshResourceLists()
    {
        StatusText = Loc.Get(LocKeys.InstanceSettings_RefreshingResources);
        LoadResourceLists();
    }

    [RelayCommand]
    private void OpenScreenshotsFolder() => OpenSubfolder("screenshots");

    [RelayCommand]
    private void OpenLogsFolder() => OpenSubfolder("logs");

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId) || !CanLaunch || _launch.IsLaunching)
            return;

        if (!_launch.HasSignedInAccount)
        {
            var root = _xamlRoot ?? _window.Content?.XamlRoot;
            if (root is not null)
            {
                await new ContentDialog
                {
                    XamlRoot = root,
                    Title = Loc.Get(LocKeys.Nav_Account),
                    Content = Loc.Get(LocKeys.Account_NeedLogin),
                    CloseButtonText = Loc.Get(LocKeys.Action_Close)
                }.ShowAsync();
            }

            return;
        }

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
            var xamlRoot = _window.Content?.XamlRoot;
            if (xamlRoot is null)
                return;

            var options = await Ardel.Launcher.Views.ModpackExportDialog.ShowAsync(xamlRoot).ConfigureAwait(true);
            if (options is null)
                return;

            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            picker.SuggestedFileName = _versionId;
            picker.FileTypeChoices.Add("Modrinth modpack", [".mrpack"]);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            StatusText = Loc.Get(LocKeys.InstanceSettings_Exporting);
            var global = _settingsService.Load();
            var path = file.Path;
            var progress = new Progress<string>(msg => StatusText = msg);
            var catalog = options.PreferThinPack ? new ModCatalogService() : null;
            await ModpackExportService.ExportMrpackAsync(
                    _versionId,
                    path,
                    options,
                    catalog,
                    global.GameDirectory,
                    progress)
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
        IconGlyph = "\uE7FC";
        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, _versionId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            item.IconGlyph = "\uE7FC";
        }
        ScheduleAutoSave();
        StatusText = Loc.Get(LocKeys.InstanceSettings_IconCleared);
    }

    [RelayCommand]
    private void SelectPresetIcon(string glyph)
    {
        if (!_loaded || string.IsNullOrWhiteSpace(_versionId) || string.IsNullOrWhiteSpace(glyph))
            return;

        InstanceIconHelper.Clear(_instanceDirectory);
        RefreshIconPreview();
        SyncIconToList(null);

        IconGlyph = glyph;
        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, _versionId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            item.IconGlyph = glyph;
        }

        ScheduleAutoSave();
        StatusText = Loc.Get(LocKeys.InstanceSettings_IconUpdated);
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
            }

            if (string.Equals(snapshot.QuickLaunchVersionId, _versionId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.QuickLaunchVersionId = newName;
            }

            _settingsService.Save(snapshot);

            _versionId = newName;
            _instanceDirectory = GamePaths.GetVersionInstanceDirectory(_versionId, global.GameDirectory);
            ApplyIdentity(_versionId, global.GameDirectory);
            EnsureResourceWatchers();

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
        MemoryMode = (int)InstanceMemoryMode.FollowDefault;
        MaxRamMb = GlobalMaxRamMb;
        ExtraJvmArguments = string.Empty;
        ExtraGameArguments = string.Empty;
        _suppressResolutionSync = true;
        ScreenWidthText = string.Empty;
        ScreenHeightText = string.Empty;
        _suppressResolutionSync = false;
        SelectedResolutionPreset = ResolutionPresets[0];
        FullScreen = false;
        WindowTitle = string.Empty;
        ServerIp = string.Empty;
        ServerPortText = string.Empty;

        RefreshMemoryPreview();
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

            var snapshot = _launch.SnapshotSettings();
            if (string.Equals(snapshot.QuickLaunchVersionId, _versionId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.QuickLaunchVersionId = null;
                _settingsService.Save(snapshot);
            }

            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
            DisposeResourceWatchers();
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
        FlushPendingWorldSave();
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
            // Auto-join / JVM args change StartInfo — drop stale ProcessStartInfo cache.
            _launch.InvalidateLaunchProcessCache(_versionId);
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
        var kind = VersionKindDetector.Detect(versionId, minecraftRoot);
        var baseId = GamePaths.ResolveBaseGameVersion(versionId, minecraftRoot);
        ApplyIdentityResolved(versionId, kind, baseId);
    }

    private void ApplyIdentityResolved(string versionId, VersionKind kind, string? baseId)
    {
        VersionIdDisplay = versionId;
        EditName = versionId;

        var item = _launch.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        var kindLabel = item?.KindLabel
                        ?? kind switch
                        {
                            VersionKind.Fabric => Loc.Get(LocKeys.Install_LoaderFabric),
                            VersionKind.Quilt => Loc.Get(LocKeys.Install_LoaderQuilt),
                            VersionKind.Forge => Loc.Get(LocKeys.Install_LoaderForge),
                            VersionKind.NeoForge => Loc.Get(LocKeys.Install_LoaderNeoForge),
                            VersionKind.OptiFine => Loc.Get(LocKeys.Install_LoaderOptiFine),
                            VersionKind.Custom => Loc.Get(LocKeys.Version_Custom),
                            _ => Loc.Get(LocKeys.Version_Vanilla)
                        };

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

    // Resolve the recommened Java major version in the background
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

    private void RefreshMemoryPreview()
    {
        if (!SystemMemory.TryGetMegabytes(out var total, out var used))
        {
            total = Math.Max(1024, SystemTotalRamMb > 0 ? SystemTotalRamMb : 16384);
            used = 0;
        }

        total = Math.Max(1024, total);
        used = Math.Clamp(used, 0, total);
        SystemTotalRamMb = total;
        MemorySliderMaximum = Math.Max(2048, Math.Min(65536, total));

        var preview = MemoryLaunchResolver.Preview(
            (InstanceMemoryMode)MemoryMode,
            GlobalMemoryMode == (int)DefaultMemoryMode.Dynamic
                ? DefaultMemoryMode.Dynamic
                : DefaultMemoryMode.Custom,
            MaxRamMb,
            GlobalMaxRamMb,
            _instanceDirectory);
        var game = Math.Clamp(preview.MaxMb, 512, MemorySliderMaximum);

        // Bar must sum to total: planned heap is carved from free RAM, not stacked on top of "in use".
        var free = Math.Max(0, total - used);
        var gameOnBar = Math.Min(game, free);
        var empty = Math.Max(0, free - gameOnBar);
        var usedShare = Math.Max(used, 0);
        var gameShare = Math.Max(gameOnBar, 0);
        var emptyShare = Math.Max(empty, 0);
        if (usedShare + gameShare + emptyShare <= 0)
        {
            usedShare = 1;
            gameShare = 0;
            emptyShare = 1;
        }

        MemoryUsedColumn = new GridLength(usedShare, GridUnitType.Star);
        MemoryGameColumn = new GridLength(gameShare, GridUnitType.Star);
        MemoryEmptyColumn = new GridLength(Math.Max(1, emptyShare), GridUnitType.Star);
        MemoryUsedText = FormatRam(used);
        MemoryGameText = FormatRam(game);
        MemoryFreeText = FormatRam(empty);
        MemoryTotalText = FormatRam(total);
    }

    private static string FormatRam(int megabytes)
    {
        megabytes = Math.Max(0, megabytes);
        if (megabytes < 1024)
            return $"{megabytes} MB";

        var gib = megabytes / 1024.0;
        // One decimal keeps used/total from rounding into impossible pairs.
        return $"{gib:0.#} GiB";
    }

    private bool TryBuildSettings(out InstanceSettings settings, out string error)
    {
        var max = Math.Clamp(MaxRamMb, 512, 65536);
        var mode = MemoryMode is >= 0 and <= 2
            ? MemoryMode
            : (int)InstanceMemoryMode.FollowDefault;

        settings = new InstanceSettings
        {
            SchemaVersion = 2,
            Notes = Notes?.Trim() ?? string.Empty,
            OverrideJava = OverrideJava,
            JavaPath = JavaPath,
            MemoryMode = mode,
            OverrideMemory = mode == (int)InstanceMemoryMode.Custom,
            MaxRamMb = max,
            MinRamMb = 0,
            ExtraJvmArguments = ExtraJvmArguments ?? string.Empty,
            ExtraGameArguments = ExtraGameArguments ?? string.Empty,
            FullScreen = FullScreen,
            WindowTitle = WindowTitle?.Trim() ?? string.Empty,
            ServerIp = ServerIp?.Trim() ?? string.Empty,
            IconGlyph = IconGlyph
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

    private readonly List<ResourceItem> _allModsList = new();
    public ObservableCollection<ResourceItem> ModsList { get; } = new();
    public ObservableCollection<ModUpdateCandidate> UpdatableMods { get; } = new();
    public InstancePackManager ResourcePacks { get; }
    public InstancePackManager ShaderPacks { get; }
    public ObservableCollection<ResourceItem> SavesList { get; } = new();

    [ObservableProperty] private bool _isSavePropertiesOpen;
    [ObservableProperty] private bool _isSavePropertiesLoading;
    [ObservableProperty] private bool _canEditSaveProperties;
    [ObservableProperty] private bool _canRestoreSaveBackup;
    [ObservableProperty] private string _savePropertiesFolderName = string.Empty;
    [ObservableProperty] private string _saveWorldName = string.Empty;
    [ObservableProperty] private string _saveSeedText = string.Empty;
    [ObservableProperty] private int _saveGameModeIndex;
    [ObservableProperty] private int _saveDifficultyIndex;
    [ObservableProperty] private bool _saveAllowCommands;
    [ObservableProperty] private string _savePropertiesError = string.Empty;
    [ObservableProperty] private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _saveCoverImage;
    [ObservableProperty] private string _saveGameVersion = string.Empty;
    [ObservableProperty] private string _saveDataVersionText = string.Empty;
    [ObservableProperty] private string _saveLastPlayedText = string.Empty;
    [ObservableProperty] private string _saveDayText = string.Empty;
    [ObservableProperty] private string _saveSpawnText = string.Empty;
    [ObservableProperty] private string _saveHardcoreText = string.Empty;

    public bool HasSaveCover => SaveCoverImage is not null;
    public bool HasSaveSeed => !string.IsNullOrWhiteSpace(SaveSeedText);
    public bool CanManageSaveCover =>
        !IsSavePropertiesLoading && !string.IsNullOrWhiteSpace(_saveWorldFolderPath);

    public IReadOnlyList<string> SaveGameModeOptions { get; } =
    [
        Loc.Get(LocKeys.InstanceSettings_SaveGameModeSurvival),
        Loc.Get(LocKeys.InstanceSettings_SaveGameModeCreative),
        Loc.Get(LocKeys.InstanceSettings_SaveGameModeAdventure),
        Loc.Get(LocKeys.InstanceSettings_SaveGameModeSpectator)
    ];

    public IReadOnlyList<string> SaveDifficultyOptions { get; } =
    [
        Loc.Get(LocKeys.InstanceSettings_SaveDifficultyPeaceful),
        Loc.Get(LocKeys.InstanceSettings_SaveDifficultyEasy),
        Loc.Get(LocKeys.InstanceSettings_SaveDifficultyNormal),
        Loc.Get(LocKeys.InstanceSettings_SaveDifficultyHard)
    ];

    // Bind Visibility directly — x:Bind + BoolToVisibilityConverter has failed open
    // (all panels Visible) and stacked every tab on top of each other.
    public Visibility SavesListVisibility =>
        SelectedTabIndex == 6 && !IsSavePropertiesOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SavePropertiesVisibility =>
        SelectedTabIndex == 6 && IsSavePropertiesOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatisticsTabVisibility =>
        SelectedTabIndex == 7 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OverviewTabVisibility =>
        SelectedTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility JavaConfigTabVisibility =>
        SelectedTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManageTabVisibility =>
        SelectedTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModsTabVisibility =>
        SelectedTabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResourcePacksTabVisibility =>
        SelectedTabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShaderPacksTabVisibility =>
        SelectedTabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<PlayDayBarItem> PlayDayBars { get; } = [];
    public ObservableCollection<ScreenshotThumbItem> ScreenshotThumbs { get; } = [];

    [ObservableProperty] private string _statsTotalPlayTimeText = "—";
    [ObservableProperty] private string _statsLaunchCountText = "0";
    [ObservableProperty] private string _statsLastPlayedText = "—";
    [ObservableProperty] private string _statsAvgSessionText = "—";
    [ObservableProperty] private bool _isStatisticsEmpty = true;
    [ObservableProperty] private bool _hasScreenshotGallery;

    public Visibility StatisticsContentVisibility =>
        IsStatisticsEmpty ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SettingsSidebarVisibility =>
        IsSavePropertiesOpen ? Visibility.Collapsed : Visibility.Visible;
    public bool HasSavePropertiesError => !string.IsNullOrWhiteSpace(SavePropertiesError);

    private LevelDatDocument? _saveDocument;
    private string _saveWorldFolderPath = string.Empty;
    private CancellationTokenSource? _worldSaveCts;
    private bool _suppressWorldSave;

    partial void OnSaveCoverImageChanged(Microsoft.UI.Xaml.Media.Imaging.BitmapImage? value) =>
        OnPropertyChanged(nameof(HasSaveCover));

    partial void OnSaveSeedTextChanged(string value) =>
        OnPropertyChanged(nameof(HasSaveSeed));

    partial void OnIsSavePropertiesLoadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanManageSaveCover));

    partial void OnSaveWorldNameChanged(string value) => ScheduleWorldSave();
    partial void OnSaveGameModeIndexChanged(int value) => ScheduleWorldSave();
    partial void OnSaveDifficultyIndexChanged(int value) => ScheduleWorldSave();
    partial void OnSaveAllowCommandsChanged(bool value) => ScheduleWorldSave();

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != 6 && IsSavePropertiesOpen)
            CloseSaveProperties();

        OnPropertyChanged(nameof(SavesListVisibility));
        OnPropertyChanged(nameof(SavePropertiesVisibility));
        OnPropertyChanged(nameof(StatisticsTabVisibility));
        OnPropertyChanged(nameof(OverviewTabVisibility));
        OnPropertyChanged(nameof(JavaConfigTabVisibility));
        OnPropertyChanged(nameof(ManageTabVisibility));
        OnPropertyChanged(nameof(ModsTabVisibility));
        OnPropertyChanged(nameof(ResourcePacksTabVisibility));
        OnPropertyChanged(nameof(ShaderPacksTabVisibility));

        if (value == 7)
            _ = LoadStatisticsAsync();
    }

    public async Task LoadStatisticsAsync()
    {
        if (string.IsNullOrWhiteSpace(_versionId))
            return;

        var minecraftRoot = _settingsService.Load().GameDirectory;
        var instanceDir = _instanceDirectory;
        var stats = await Task.Run(() => _statsStore.Load(_versionId, minecraftRoot)).ConfigureAwait(true);

        StatsTotalPlayTimeText = FormatDuration(stats.TotalPlaySeconds);
        StatsLaunchCountText = stats.LaunchCount.ToString();
        StatsLastPlayedText = stats.LastSessionEndUtc is DateTime end
            ? end.ToLocalTime().ToString("g")
            : "—";
        StatsAvgSessionText = stats.LaunchCount > 0
            ? FormatDuration(stats.TotalPlaySeconds / stats.LaunchCount)
            : "—";
        IsStatisticsEmpty = stats.LaunchCount == 0 && stats.TotalPlaySeconds == 0;
        OnPropertyChanged(nameof(StatisticsContentVisibility));

        PlayDayBars.Clear();
        var today = DateTime.UtcNow.Date;
        var daySeconds = new int[14];
        foreach (var session in stats.Sessions)
        {
            var day = session.EndUtc?.Date ?? session.StartUtc.Date;
            var index = (int)(today - day).TotalDays;
            if (index is >= 0 and < 14)
                daySeconds[13 - index] += session.DurationSeconds;
        }

        var max = Math.Max(1, daySeconds.Max());
        for (var i = 0; i < 14; i++)
        {
            var day = today.AddDays(i - 13);
            var seconds = daySeconds[i];
            var label = day.ToString("MM/dd");
            var durationText = FormatDuration(seconds);
            PlayDayBars.Add(new PlayDayBarItem
            {
                Label = label,
                Seconds = seconds,
                BarHeight = seconds > 0 ? 8 + (72.0 * seconds / max) : 6,
                DurationText = durationText,
                ToolTip = Loc.Format(LocKeys.InstanceSettings_StatsDayTooltip, label, durationText)
            });
        }

        await LoadScreenshotThumbsAsync(instanceDir).ConfigureAwait(true);
    }

    private async Task LoadScreenshotThumbsAsync(string instanceDir)
    {
        ScreenshotThumbs.Clear();
        HasScreenshotGallery = false;

        if (string.IsNullOrWhiteSpace(instanceDir))
            return;

        var screenshotsDir = Path.Combine(instanceDir, "screenshots");
        var paths = await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(screenshotsDir))
                    return Array.Empty<string>();

                return Directory.EnumerateFiles(screenshotsDir)
                    .Where(static p =>
                    {
                        var ext = Path.GetExtension(p);
                        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(12)
                    .Select(f => f.FullName)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettings] Screenshots scan: {ex.Message}");
                return Array.Empty<string>();
            }
        }).ConfigureAwait(true);

        foreach (var path in paths)
        {
            var image = InstanceIconHelper.CreateImage(path, decodePixels: 128);
            if (image is null)
                continue;

            ScreenshotThumbs.Add(new ScreenshotThumbItem
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Image = image
            });
        }

        HasScreenshotGallery = ScreenshotThumbs.Count > 0;
    }

    private static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds <= 0)
            return "0m";

        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{Math.Max(1, ts.Minutes)}m";
    }

    partial void OnIsStatisticsEmptyChanged(bool value) =>
        OnPropertyChanged(nameof(StatisticsContentVisibility));

    partial void OnIsSavePropertiesOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(SavesListVisibility));
        OnPropertyChanged(nameof(SavePropertiesVisibility));
        OnPropertyChanged(nameof(SettingsSidebarVisibility));
    }

    partial void OnSavePropertiesErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasSavePropertiesError));

    public async Task OpenSavePropertiesAsync(ResourceItem save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!save.IsWorldSave)
            return;

        _saveWorldFolderPath = save.FullPath;
        OnPropertyChanged(nameof(CanManageSaveCover));
        _saveDocument = null;
        SavePropertiesFolderName = save.FileName;
        SaveWorldName = string.Empty;
        SaveSeedText = string.Empty;
        SaveGameModeIndex = 0;
        SaveDifficultyIndex = 2;
        SaveAllowCommands = false;
        SavePropertiesError = string.Empty;
        SaveGameVersion = string.Empty;
        SaveDataVersionText = string.Empty;
        SaveLastPlayedText = string.Empty;
        SaveDayText = string.Empty;
        SaveSpawnText = string.Empty;
        SaveHardcoreText = string.Empty;
        CanEditSaveProperties = false;
        CanRestoreSaveBackup = false;
        SaveCoverImage = null;
        IsSavePropertiesLoading = true;
        IsSavePropertiesOpen = true;

        var folder = save.FullPath;
        LevelDatDocument? document = null;
        var hasBackup = false;
        try
        {
            await Task.Run(() =>
            {
                document = LevelDatService.TryLoad(folder);
                hasBackup = LevelDatService.HasReadableBackup(folder);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] Load save properties error: {ex.Message}");
        }

        if (!string.Equals(_saveWorldFolderPath, folder, StringComparison.OrdinalIgnoreCase))
            return;

        _saveDocument = document;
        CanRestoreSaveBackup = document is null && hasBackup;
        IsSavePropertiesLoading = false;
        OnPropertyChanged(nameof(CanManageSaveCover));

        if (document is null)
        {
            RefreshSaveCover();
            SavePropertiesError = Loc.Get(LocKeys.InstanceSettings_SaveNoLevelDat);
            return;
        }

        ApplySaveMetadata(document.ToMetadata(), save.FileName);
        CanEditSaveProperties = true;
        RefreshSaveCover();
    }

    [RelayCommand]
    private void CloseSaveProperties()
    {
        FlushPendingWorldSave();
        IsSavePropertiesOpen = false;
        IsSavePropertiesLoading = false;
        CanEditSaveProperties = false;
        CanRestoreSaveBackup = false;
        SaveCoverImage = null;
        _saveDocument = null;
        _saveWorldFolderPath = string.Empty;
        SavePropertiesError = string.Empty;
        SaveGameVersion = string.Empty;
        SaveDataVersionText = string.Empty;
        SaveLastPlayedText = string.Empty;
        SaveDayText = string.Empty;
        SaveSpawnText = string.Empty;
        SaveHardcoreText = string.Empty;
        OnPropertyChanged(nameof(CanManageSaveCover));
    }

    [RelayCommand]
    private void RestoreSaveBackup()
    {
        if (string.IsNullOrWhiteSpace(_saveWorldFolderPath))
            return;

        try
        {
            var restored = LevelDatService.RestoreBackup(_saveWorldFolderPath);
            if (restored is null)
            {
                SavePropertiesError = Loc.Get(LocKeys.InstanceSettings_SaveNoLevelDat);
                CanEditSaveProperties = false;
                return;
            }

            _saveDocument = restored;
            ApplySaveMetadata(restored.ToMetadata(), SavePropertiesFolderName);
            CanEditSaveProperties = true;
            CanRestoreSaveBackup = false;
            SavePropertiesError = string.Empty;
            StatusText = Loc.Get(LocKeys.InstanceSettings_SaveRestored);
        }
        catch (Exception ex)
        {
            SavePropertiesError = Loc.Format(LocKeys.InstanceSettings_SaveSaveFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void SaveWorldProperties() => PersistWorldProperties(showStatus: true);

    private void ScheduleWorldSave()
    {
        if (_suppressWorldSave || !CanEditSaveProperties || _saveDocument is null)
            return;

        CancelPendingWorldSave();
        var cts = new CancellationTokenSource();
        _worldSaveCts = cts;
        _ = DebouncedWorldPersistAsync(cts.Token);
    }

    private async Task DebouncedWorldPersistAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(AutoSaveDelayMs, token).ConfigureAwait(true);
            PersistWorldProperties(showStatus: true);
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
    }

    private void FlushPendingWorldSave()
    {
        CancelPendingWorldSave();
        if (CanEditSaveProperties && _saveDocument is not null)
            PersistWorldProperties(showStatus: false);
    }

    private void CancelPendingWorldSave()
    {
        try
        {
            _worldSaveCts?.Cancel();
            _worldSaveCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }

        _worldSaveCts = null;
    }

    private void PersistWorldProperties(bool showStatus)
    {
        if (_saveDocument is null)
            return;

        var levelName = SaveWorldName.Trim();
        if (string.IsNullOrWhiteSpace(levelName))
        {
            SavePropertiesError = Loc.Get(LocKeys.InstanceSettings_SaveInvalidWorldName);
            return;
        }

        try
        {
            LevelDatService.Save(_saveDocument, new SaveWorldPropertiesEdit
            {
                LevelName = levelName,
                GameType = Math.Clamp(SaveGameModeIndex, 0, 3),
                Difficulty = Math.Clamp(SaveDifficultyIndex, 0, 3),
                AllowCommands = SaveAllowCommands
            });

            SavePropertiesError = string.Empty;
            if (showStatus)
                StatusText = Loc.Get(LocKeys.InstanceSettings_Saved);
            LoadResourceLists();
        }
        catch (Exception ex)
        {
            SavePropertiesError = Loc.Format(LocKeys.InstanceSettings_SaveSaveFailed, ex.Message);
        }
    }

    private void ApplySaveMetadata(SaveWorldMetadata metadata, string folderName)
    {
        _suppressWorldSave = true;
        try
        {
            SaveWorldName = string.IsNullOrWhiteSpace(metadata.LevelName) ? folderName : metadata.LevelName;
            SaveSeedText = metadata.Seed?.ToString() ?? string.Empty;
            SaveGameModeIndex = metadata.GameType is >= 0 and <= 3 ? metadata.GameType.Value : 0;
            SaveDifficultyIndex = metadata.Difficulty is >= 0 and <= 3 ? metadata.Difficulty.Value : 2;
            SaveAllowCommands = metadata.AllowCommands ?? false;

            SaveGameVersion = string.IsNullOrWhiteSpace(metadata.GameVersion) ? Loc.Get(LocKeys.InstanceSettings_SaveUnknown) : metadata.GameVersion;
            SaveDataVersionText = metadata.DataVersion?.ToString() ?? Loc.Get(LocKeys.InstanceSettings_SaveUnknown);
            SaveHardcoreText = metadata.Hardcore == true ? Loc.Get(LocKeys.InstanceSettings_SaveValueYes) : Loc.Get(LocKeys.InstanceSettings_SaveValueNo);

            if (metadata.LastPlayed is long lp)
            {
                try
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(lp).LocalDateTime;
                    SaveLastPlayedText = dt.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch
                {
                    SaveLastPlayedText = Loc.Get(LocKeys.InstanceSettings_SaveUnknown);
                }
            }
            else
            {
                SaveLastPlayedText = Loc.Get(LocKeys.InstanceSettings_SaveUnknown);
            }

            if (metadata.Time is long t)
            {
                long day = (t / 24000) + 1;
                SaveDayText = day.ToString();
            }
            else
            {
                SaveDayText = Loc.Get(LocKeys.InstanceSettings_SaveUnknown);
            }

            if (metadata.SpawnX is int x && metadata.SpawnY is int y && metadata.SpawnZ is int z)
            {
                SaveSpawnText = $"X: {x}, Y: {y}, Z: {z}";
            }
            else if (metadata.SpawnX is int x2 && metadata.SpawnZ is int z2)
            {
                SaveSpawnText = $"X: {x2}, Y: ?, Z: {z2}";
            }
            else
            {
                SaveSpawnText = Loc.Get(LocKeys.InstanceSettings_SaveUnknown);
            }
        }
        finally
        {
            _suppressWorldSave = false;
        }
    }

    private void RefreshSaveCover()
    {
        if (string.IsNullOrWhiteSpace(_saveWorldFolderPath))
        {
            SaveCoverImage = null;
            return;
        }

        SaveCoverImage = SaveIconHelper.CreateImage(SaveIconHelper.FindPath(_saveWorldFolderPath));
    }

    [RelayCommand]
    private async Task BrowseSaveCoverAsync()
    {
        if (string.IsNullOrWhiteSpace(_saveWorldFolderPath))
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

            await SaveIconHelper.SetFromFileAsync(_saveWorldFolderPath, file.Path).ConfigureAwait(true);
            RefreshSaveCover();
            StatusText = Loc.Get(LocKeys.InstanceSettings_IconUpdated);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            SavePropertiesError = Loc.Format(LocKeys.InstanceSettings_IconFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void ClearSaveCover()
    {
        if (string.IsNullOrWhiteSpace(_saveWorldFolderPath))
            return;

        SaveIconHelper.Clear(_saveWorldFolderPath);
        RefreshSaveCover();
        StatusText = Loc.Get(LocKeys.InstanceSettings_IconCleared);
    }

    [ObservableProperty] private int _modFilterIndex;
    [ObservableProperty] private bool _hasUpdatableMods;
    [ObservableProperty] private bool _isCheckingModUpdates;
    [ObservableProperty] private bool _isUpdatingMods;
    [ObservableProperty] private string _modUpdateStatusText = string.Empty;
    [ObservableProperty] private string _updatableFilterLabel = string.Empty;
    private string _modUpdateResultText = string.Empty;

    public bool HasModUpdateStatus => !string.IsNullOrWhiteSpace(ModUpdateStatusText);
    public bool ShowRegularModsList => ModFilterIndex is >= 0 and <= 2;
    public bool ShowUpdatableModsList => ModFilterIndex == 3;
    public bool CanUpdateSelectedMods =>
        !IsUpdatingMods && UpdatableMods.Any(m => m.IsSelected);
    public bool IsVanillaProfile =>
        VersionKindDetector.Detect(_versionId) == VersionKind.Vanilla;
    public string EmptyModsMessage =>
        IsVanillaProfile
            ? Loc.Get(LocKeys.InstanceSettings_EmptyModsVanilla)
            : Loc.Get(LocKeys.InstanceSettings_EmptyMods);
    public bool IsModsListEmpty => ShowRegularModsList && ModsList.Count == 0;
    public bool IsSavesEmpty => SavesList.Count == 0;

    private CancellationTokenSource? _modUpdateCheckCts;

    partial void OnModUpdateStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasModUpdateStatus));

    partial void OnModFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowRegularModsList));
        OnPropertyChanged(nameof(ShowUpdatableModsList));
        if (value is >= 0 and <= 2)
            ApplyModFilter();
        else
            ClearModSelection();
        UpdateCanUpdateSelected();
    }

    private void ApplyModFilter()
    {
        IEnumerable<ResourceItem> query = _allModsList;
        if (ModFilterIndex == 1)
            query = _allModsList.Where(m => m.IsEnabled);
        else if (ModFilterIndex == 2)
            query = _allModsList.Where(m => !m.IsEnabled);

        var items = query.ToList();
        foreach (var item in items)
        {
            item.IsSelected = false;
            item.SelectionChanged = ScheduleModSelectionRefresh;
        }

        ReplaceCollection(ModsList, items);
        RefreshModSelectionStateCore();
        OnPropertyChanged(nameof(IsModsListEmpty));
        OnPropertyChanged(nameof(EmptyModsMessage));
        OnPropertyChanged(nameof(IsVanillaProfile));
    }

    public bool HasSelectedMods => ModsList.Any(m => m.IsSelected);
    public bool CanEnableSelectedMods => ModsList.Any(m => m.IsSelected && !m.IsEnabled);
    public bool CanDisableSelectedMods => ModsList.Any(m => m.IsSelected && m.IsEnabled);
    public bool ShowModBrowseActions => true;
    public bool ShowModSelectionActions => HasSelectedMods;
    public string SelectedModsCountText =>
        Loc.Format(LocKeys.InstanceSettings_SelectedCount, ModsList.Count(m => m.IsSelected));

    public void RefreshModSelectionState() => ScheduleModSelectionRefresh();

    private void ScheduleModSelectionRefresh() => _modSelectionCoalesce.Schedule();

    private void RefreshModSelectionStateCore()
    {
        OnPropertyChanged(nameof(HasSelectedMods));
        OnPropertyChanged(nameof(CanEnableSelectedMods));
        OnPropertyChanged(nameof(CanDisableSelectedMods));
        OnPropertyChanged(nameof(ShowModSelectionActions));
        OnPropertyChanged(nameof(SelectedModsCountText));
        EnableSelectedModsCommand.NotifyCanExecuteChanged();
        DisableSelectedModsCommand.NotifyCanExecuteChanged();
        DeleteSelectedModsCommand.NotifyCanExecuteChanged();
        ClearSelectedModsCommand.NotifyCanExecuteChanged();
    }

    public void ClearModSelection()
    {
        foreach (var m in ModsList)
            m.IsSelected = false;
        RefreshModSelectionStateCore();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private void ClearSelectedMods() => ClearModSelection();

    [RelayCommand(CanExecute = nameof(CanEnableSelectedMods))]
    private void EnableSelectedMods()
    {
        var targets = ModsList.Where(m => m.IsSelected && !m.IsEnabled).ToList();
        if (targets.Count == 0)
            return;

        foreach (var m in targets)
            m.SetEnabledForBatch(true);

        ClearModSelection();
        LoadResourceLists();
    }

    [RelayCommand(CanExecute = nameof(CanDisableSelectedMods))]
    private void DisableSelectedMods()
    {
        var targets = ModsList.Where(m => m.IsSelected && m.IsEnabled).ToList();
        if (targets.Count == 0)
            return;

        foreach (var m in targets)
            m.SetEnabledForBatch(false);

        ClearModSelection();
        LoadResourceLists();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private async Task DeleteSelectedModsAsync()
    {
        var targets = ModsList.Where(m => m.IsSelected).ToList();
        if (targets.Count == 0)
            return;

        if (!await ConfirmDialog.ConfirmDeleteAsync(targets.Select(t => t.DisplayName).ToList())
                .ConfigureAwait(true))
            return;

        foreach (var m in targets)
        {
            try
            {
                if (File.Exists(m.FullPath) || Directory.Exists(m.FullPath))
                    RecycleBinHelper.MoveToRecycleBin(m.FullPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettingsViewModel] DeleteSelectedMods {m.FileName}: {ex.Message}");
            }
        }

        ClearModSelection();
        LoadResourceLists();
    }

    private void RefreshUpdatableFilterLabel()
    {
        UpdatableFilterLabel = HasUpdatableMods
            ? Loc.Format(LocKeys.InstanceSettings_ModFilterUpdatableCount, UpdatableMods.Count)
            : Loc.Get(LocKeys.InstanceSettings_ModFilterUpdatable);
    }

    private void UpdateCanUpdateSelected()
    {
        OnPropertyChanged(nameof(CanUpdateSelectedMods));
        UpdateSelectedModsCommand.NotifyCanExecuteChanged();
    }

    public void LoadResourceLists()
    {
        if (string.IsNullOrWhiteSpace(_versionId))
            return;

        _modUpdateCheckCts?.Cancel();
        _modUpdateCheckCts = new CancellationTokenSource();
        _resourceLoadCts?.Cancel();
        _resourceLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_modUpdateCheckCts.Token);
        _ = LoadResourceListsAsync(_resourceLoadCts.Token);
    }

    private void EnsureResourceWatchers()
    {
        if (string.IsNullOrWhiteSpace(_instanceDirectory))
            return;

        try
        {
            Directory.CreateDirectory(_instanceDirectory);
            foreach (var sub in new[] { "mods", "resourcepacks", "shaderpacks", "saves" })
                Directory.CreateDirectory(Path.Combine(_instanceDirectory, sub));

            if (_resourceWatcher is not null &&
                string.Equals(_resourceWatcherRoot, _instanceDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeResourceWatchers();

            var watcher = new FileSystemWatcher(_instanceDirectory)
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size,
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024
            };
            watcher.Created += OnResourceFileSystemEvent;
            watcher.Deleted += OnResourceFileSystemEvent;
            watcher.Changed += OnResourceFileSystemEvent;
            watcher.Renamed += OnResourceFileSystemEvent;
            watcher.Error += (_, e) =>
                Debug.WriteLine($"[InstanceSettings] Resource watcher error: {e.GetException().Message}");
            watcher.EnableRaisingEvents = true;

            _resourceWatcher = watcher;
            _resourceWatcherRoot = _instanceDirectory;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettings] Resource watcher setup failed: {ex.Message}");
        }
    }

    private void DisposeResourceWatchers()
    {
        try
        {
            _resourceReloadTimer?.Stop();
        }
        catch
        {
            // ignore
        }

        if (_resourceWatcher is null)
        {
            _resourceWatcherRoot = null;
            return;
        }

        try
        {
            _resourceWatcher.EnableRaisingEvents = false;
            _resourceWatcher.Created -= OnResourceFileSystemEvent;
            _resourceWatcher.Deleted -= OnResourceFileSystemEvent;
            _resourceWatcher.Changed -= OnResourceFileSystemEvent;
            _resourceWatcher.Renamed -= OnResourceFileSystemEvent;
            _resourceWatcher.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettings] Resource watcher dispose failed: {ex.Message}");
        }

        _resourceWatcher = null;
        _resourceWatcherRoot = null;
    }

    private void OnResourceFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!ShouldReloadForResourceEvent(e))
            return;

        var dispatcher = _dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.TryEnqueue(() =>
        {
            if (_resourceReloadTimer is null || !_loaded)
                return;
            _resourceReloadTimer.Stop();
            _resourceReloadTimer.Start();
        });
    }

    private static bool ShouldReloadForResourceEvent(FileSystemEventArgs e)
    {
        var relative = e.Name;
        if (string.IsNullOrWhiteSpace(relative))
            return false;

        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var top = parts[0];
        return top.Equals("mods", StringComparison.OrdinalIgnoreCase)
               || top.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase)
               || top.Equals("shaderpacks", StringComparison.OrdinalIgnoreCase)
               || top.Equals("saves", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadResourceListsAsync(CancellationToken cancellationToken)
    {
        var versionId = _versionId;
        var gameDir = _settingsService.Load().GameDirectory;

        ResourceSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rootDir = GamePaths.GetVersionInstanceDirectory(versionId, gameDir);
                var mods = new List<ResourceItem>();
                PopulateModsList(mods, Path.Combine(rootDir, "mods"));
                return new ResourceSnapshot(
                    mods,
                    InstancePackManager.Scan(Path.Combine(rootDir, "resourcepacks"), CatalogProjectKind.ResourcePack),
                    InstancePackManager.Scan(Path.Combine(rootDir, "shaderpacks"), CatalogProjectKind.ShaderPack),
                    ScanFolders(Path.Combine(rootDir, "saves")));
            }, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ClearRefreshingStatus();
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] LoadResourceListsAsync: {ex.Message}");
            ClearRefreshingStatus();
            return;
        }

        if (cancellationToken.IsCancellationRequested || !string.Equals(_versionId, versionId, StringComparison.Ordinal))
        {
            ClearRefreshingStatus();
            return;
        }

        _allModsList.Clear();
        _allModsList.AddRange(snapshot.Mods);
        if (ModFilterIndex == 3)
            ModFilterIndex = 0;
        else
            ApplyModFilter();

        UpdatableMods.Clear();
        HasUpdatableMods = false;
        RefreshUpdatableFilterLabel();
        ResourcePacks.ReplaceAll(snapshot.Resourcepacks);
        ShaderPacks.ReplaceAll(snapshot.Shaderpacks);
        ReplaceCollection(SavesList, snapshot.Saves);
        OnPropertyChanged(nameof(IsSavesEmpty));

        ClearRefreshingStatus();

        _ = MatchModsOnlineAsync(cancellationToken);
        _ = MatchAllPacksOnlineAsync(cancellationToken);
    }

    private void ClearRefreshingStatus()
    {
        if (string.Equals(
                StatusText,
                Loc.Get(LocKeys.InstanceSettings_RefreshingResources),
                StringComparison.Ordinal))
        {
            StatusText = string.Empty;
        }
    }

    private readonly record struct ResourceSnapshot(
        List<ResourceItem> Mods,
        List<ResourceItem> Resourcepacks,
        List<ResourceItem> Shaderpacks,
        List<ResourceItem> Saves);

    private static void ReplaceCollection(ObservableCollection<ResourceItem> target, List<ResourceItem> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private static void PopulateModsList(List<ResourceItem> list, string folderPath)
    {
        list.Clear();
        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var files = Directory.GetFiles(folderPath, "*.*")
                .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                .Select(p => new FileInfo(p))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
            {
                var sizeStr = f.Length > 1024 * 1024
                    ? $"{f.Length / (1024.0 * 1024.0):F1} MB"
                    : $"{f.Length / 1024.0:F1} KB";
                list.Add(new ResourceItem(f.Name, sizeStr, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), f.FullName, isToggleable: true));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] PopulateModsList error: {ex.Message}");
        }
    }

    private async Task MatchModsOnlineAsync(CancellationToken cancellationToken)
    {
        // Match against the full catalog so filter tabs stay accurate after metadata arrives.
        var items = _allModsList.ToList();
        if (items.Count == 0)
        {
            HasUpdatableMods = false;
            RefreshUpdatableFilterLabel();
            return;
        }

        var catalogService = new ModCatalogService();
        using var gate = new SemaphoreSlim(4);
        var dispatcher = _dispatcher ?? DispatcherQueue.GetForCurrentThread();

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ZIP/JSON parse off UI thread.
                var (localTitle, localIconPath, localId) = await Task.Run(
                    () => TryExtractJarMeta(item.FullPath),
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(localTitle) || !string.IsNullOrEmpty(localIconPath))
                {
                    EnqueueUi(dispatcher, () =>
                    {
                        if (!string.IsNullOrEmpty(localTitle))
                            item.DisplayName = localTitle;
                        if (!string.IsNullOrEmpty(localIconPath))
                            item.IconUrl = localIconPath;
                    });
                }

                var match = await CatalogLocalMatcher.MatchAsync(
                    catalogService,
                    item.FullPath,
                    item.FileName,
                    localTitle,
                    localId,
                    CatalogProjectKind.Mod,
                    cancellationToken).ConfigureAwait(false);

                if (match is not null)
                {
                    EnqueueUi(dispatcher, () =>
                    {
                        item.DisplayName = match.Title;
                        if (!string.IsNullOrEmpty(match.IconUrl))
                            item.IconUrl = match.IconUrl;
                        item.Description = match.Description;
                        item.WebPageUrl = match.WebPageUrl;
                        item.MatchedProject = match;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettingsViewModel] MatchModsOnlineAsync error for {item.FileName}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await DetectModUpdatesAsync(catalogService, cancellationToken).ConfigureAwait(true);
    }

    private async Task MatchAllPacksOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(
                MatchPackManagerAsync(ResourcePacks, cancellationToken),
                MatchPackManagerAsync(ShaderPacks, cancellationToken)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Reload cancelled.
        }
    }

    private async Task MatchPackManagerAsync(InstancePackManager manager, CancellationToken cancellationToken)
    {
        var items = manager.AllItems.ToList();
        if (items.Count == 0)
        {
            manager.SetUpdatable(Array.Empty<ModUpdateCandidate>());
            return;
        }

        manager.UpdateStatusText = Loc.Get(LocKeys.InstanceSettings_PackUpdateChecking);
        var catalogService = new ModCatalogService();
        using var gate = new SemaphoreSlim(4);
        var dispatcher = _dispatcher ?? DispatcherQueue.GetForCurrentThread();

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (localTitle, localIconPath) = await Task.Run(
                    () => InstancePackManager.TryExtractMeta(item.FullPath),
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(localTitle) || !string.IsNullOrEmpty(localIconPath))
                {
                    EnqueueUi(dispatcher, () =>
                    {
                        if (!string.IsNullOrEmpty(localTitle))
                            item.Description = localTitle;
                        if (!string.IsNullOrEmpty(localIconPath))
                            item.IconUrl = localIconPath;
                    });
                }

                var match = await CatalogLocalMatcher.MatchAsync(
                    catalogService,
                    item.FullPath,
                    item.FileName,
                    localTitle,
                    localId: null,
                    manager.Kind,
                    cancellationToken).ConfigureAwait(false);

                if (match is not null)
                {
                    EnqueueUi(dispatcher, () =>
                    {
                        item.DisplayName = match.Title;
                        if (!string.IsNullOrEmpty(match.IconUrl))
                            item.IconUrl = match.IconUrl;
                        item.Description = match.Description;
                        item.WebPageUrl = match.WebPageUrl;
                        item.MatchedProject = match;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettingsViewModel] MatchPack {item.FileName}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var updates = await FindUpdateCandidatesAsync(items, catalogService, loaderSlug: null, cancellationToken)
            .ConfigureAwait(true);
        manager.SetUpdatable(updates);
    }

    private static void EnqueueUi(DispatcherQueue? dispatcher, Action action)
    {
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(() => action());
    }

    private async Task DetectModUpdatesAsync(ModCatalogService catalog, CancellationToken cancellationToken)
    {
        IsCheckingModUpdates = true;
        if (string.IsNullOrEmpty(_modUpdateResultText))
            ModUpdateStatusText = Loc.Get(LocKeys.InstanceSettings_ModUpdateChecking);
        UpdatableMods.Clear();
        HasUpdatableMods = false;
        RefreshUpdatableFilterLabel();

        try
        {
            var loaderSlug = VersionKindDetector.Detect(_versionId) switch
            {
                VersionKind.Fabric => "fabric",
                VersionKind.Quilt => "quilt",
                VersionKind.Forge => "forge",
                VersionKind.NeoForge => "neoforge",
                _ => null
            };

            var found = await FindUpdateCandidatesAsync(_allModsList, catalog, loaderSlug, cancellationToken)
                .ConfigureAwait(true);

            foreach (var candidate in found)
                UpdatableMods.Add(candidate);

            HasUpdatableMods = UpdatableMods.Count > 0;
            RefreshUpdatableFilterLabel();
            UpdateCanUpdateSelected();
            foreach (var c in UpdatableMods)
                c.SelectionChanged = UpdateCanUpdateSelected;
        }
        finally
        {
            IsCheckingModUpdates = false;
            var scan = HasUpdatableMods
                ? Loc.Format(LocKeys.InstanceSettings_ModUpdateAvailable, UpdatableMods.Count)
                : string.Empty;
            ModUpdateStatusText = !string.IsNullOrEmpty(_modUpdateResultText)
                ? (string.IsNullOrEmpty(scan) ? _modUpdateResultText : _modUpdateResultText + " · " + scan)
                : scan;
        }
    }

    private async Task<List<ModUpdateCandidate>> FindUpdateCandidatesAsync(
        IReadOnlyList<ResourceItem> items,
        ModCatalogService catalog,
        string? loaderSlug,
        CancellationToken cancellationToken)
    {
        var mcVersion = VersionKindDetector.DetectBaseGameVersion(_versionId);
        if (string.IsNullOrWhiteSpace(mcVersion))
            return [];

        var matched = items.Where(m => m.MatchedProject is not null).ToList();
        if (matched.Count == 0)
            return [];

        using var gate = new SemaphoreSlim(4);
        var found = new System.Collections.Concurrent.ConcurrentBag<ModUpdateCandidate>();

        var tasks = matched.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var project = item.MatchedProject!;
                var detail = await catalog.GetProjectDetailAsync(
                        project,
                        cancellationToken,
                        preferredGameVersion: mcVersion,
                        preferredLoaderSlug: loaderSlug)
                    .ConfigureAwait(false);

                var best = PickBestCompatibleFile(detail.Files, mcVersion, loaderSlug);
                if (best is null || string.IsNullOrWhiteSpace(best.DownloadUrl))
                    return;

                if (!IsNewerThanInstalled(item, best))
                    return;

                var localName = item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? item.FileName[..^".disabled".Length]
                    : item.FileName;

                found.Add(new ModUpdateCandidate
                {
                    LocalMod = item,
                    DisplayName = item.DisplayName,
                    CurrentFileName = localName,
                    NewFileName = best.FileName,
                    CurrentVersionLabel = localName,
                    NewVersionLabel = best.DisplayName,
                    DownloadUrl = best.DownloadUrl,
                    IconUrl = item.IconUrl ?? project.IconUrl
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstanceSettingsViewModel] FindUpdates {item.FileName}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        return found
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModFileVersionItem? PickBestCompatibleFile(
        IReadOnlyList<ModFileVersionItem> files,
        string mcVersion,
        string? loaderSlug) =>
        ModFilePicker.PickBestCompatibleFile(files, mcVersion, loaderSlug);

    private static bool IsNewerThanInstalled(ResourceItem local, ModFileVersionItem remote)
    {
        var localName = local.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? local.FileName[..^".disabled".Length]
            : local.FileName;

        string? localSha1 = null;
        try
        {
            if (File.Exists(local.FullPath))
            {
                using var stream = File.OpenRead(local.FullPath);
                using var sha = System.Security.Cryptography.SHA1.Create();
                localSha1 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }
        }
        catch
        {
            // ignore hash failures
        }

        if (!string.IsNullOrEmpty(remote.Sha1) && !string.IsNullOrEmpty(localSha1))
            return !string.Equals(remote.Sha1, localSha1, StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(localName, remote.FileName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Same filename, no hash — treat as up-to-date.
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanUpdateSelectedMods))]
    private async Task UpdateSelectedModsAsync()
    {
        var selected = UpdatableMods.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0 || IsUpdatingMods)
            return;

        IsUpdatingMods = true;
        UpdateSelectedModsCommand.NotifyCanExecuteChanged();
        var catalog = new ModCatalogService();
        var modsDir = Path.Combine(
            GamePaths.GetVersionInstanceDirectory(_versionId, _settingsService.Load().GameDirectory),
            "mods");
        Directory.CreateDirectory(modsDir);

        var ok = 0;
        var fail = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var candidate = selected[i];
                ModUpdateStatusText = Loc.Format(
                    LocKeys.InstanceSettings_ModUpdateProgress,
                    i + 1,
                    selected.Count,
                    candidate.DisplayName);

                try
                {
                    var wasDisabled = candidate.LocalMod.FullPath
                        .EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var enabledDest = Path.Combine(modsDir, candidate.NewFileName);
                    await catalog.DownloadFileAsync(
                            candidate.DownloadUrl,
                            enabledDest,
                            progress: null,
                            CancellationToken.None)
                        .ConfigureAwait(true);

                    var finalPath = enabledDest;
                    if (wasDisabled)
                    {
                        var disabledPath = enabledDest + ".disabled";
                        if (File.Exists(disabledPath) &&
                            !string.Equals(disabledPath, candidate.LocalMod.FullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            RecycleBinHelper.MoveToRecycleBin(disabledPath);
                        }

                        if (File.Exists(enabledDest))
                        {
                            if (File.Exists(disabledPath))
                                File.Delete(disabledPath);
                            File.Move(enabledDest, disabledPath);
                            finalPath = disabledPath;
                        }
                    }

                    var oldPath = candidate.LocalMod.FullPath;
                    if (File.Exists(oldPath) &&
                        !string.Equals(oldPath, finalPath, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(oldPath, enabledDest, StringComparison.OrdinalIgnoreCase))
                    {
                        RecycleBinHelper.MoveToRecycleBin(oldPath);
                    }

                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Debug.WriteLine($"[InstanceSettingsViewModel] Update mod failed {candidate.DisplayName}: {ex.Message}");
                }
            }

            ModUpdateStatusText = fail == 0
                ? Loc.Format(LocKeys.InstanceSettings_ModUpdateDone, ok)
                : Loc.Format(LocKeys.InstanceSettings_ModUpdateDoneWithErrors, ok, fail);
            _modUpdateResultText = ModUpdateStatusText;
        }
        finally
        {
            IsUpdatingMods = false;
            UpdateSelectedModsCommand.NotifyCanExecuteChanged();
            LoadResourceLists();
        }
    }

    [RelayCommand]
    private void SelectAllUpdatableMods()
    {
        foreach (var m in UpdatableMods)
            m.IsSelected = true;
        UpdateCanUpdateSelected();
        UpdateSelectedModsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearUpdatableModsSelection()
    {
        foreach (var m in UpdatableMods)
            m.IsSelected = false;
        UpdateCanUpdateSelected();
        UpdateSelectedModsCommand.NotifyCanExecuteChanged();
    }

    private static (string? Title, string? IconPath, string? ModId) TryExtractJarMeta(string jarPath)
    {
        if (!File.Exists(jarPath)) return (null, null, null);

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(jarPath);
            string? title = null;
            string? iconName = null;
            string? modId = null;

            // 1. Fabric mod (fabric.mod.json)
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var stream = fabricEntry.Open();
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    modId = idProp.GetString();
                if (doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    title = nameProp.GetString();
                }
                if (doc.RootElement.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    iconName = iconProp.GetString();
                }
            }

            // 2. Quilt mod (quilt.mod.json)
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(modId))
            {
                var quiltEntry = archive.GetEntry("quilt.mod.json");
                if (quiltEntry is not null)
                {
                    using var stream = quiltEntry.Open();
                    using var doc = System.Text.Json.JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("quilt_loader", out var loaderProp))
                    {
                        if (string.IsNullOrEmpty(modId) &&
                            loaderProp.TryGetProperty("id", out var qid) &&
                            qid.ValueKind == System.Text.Json.JsonValueKind.String)
                            modId = qid.GetString();

                        if (loaderProp.TryGetProperty("metadata", out var metaProp))
                        {
                            if (string.IsNullOrEmpty(title) &&
                                metaProp.TryGetProperty("name", out var nameProp) &&
                                nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                title = nameProp.GetString();
                            }
                            if (metaProp.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                iconName = iconProp.GetString();
                            }
                        }
                    }
                }
            }

            // 3. Forge / NeoForge mod (META-INF/mods.toml)
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(modId))
            {
                var modsTomlEntry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("META-INF/neoforge.mods.toml");
                if (modsTomlEntry is not null)
                {
                    using var reader = new StreamReader(modsTomlEntry.Open());
                    var content = reader.ReadToEnd();

                    if (string.IsNullOrEmpty(modId))
                    {
                        var modIdMatch = System.Text.RegularExpressions.Regex.Match(content, @"modId\s*=\s*""([^""]+)""");
                        if (modIdMatch.Success)
                            modId = modIdMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(title))
                    {
                        var displayNameMatch = System.Text.RegularExpressions.Regex.Match(content, @"displayName\s*=\s*""([^""]+)""");
                        if (displayNameMatch.Success)
                            title = displayNameMatch.Groups[1].Value;
                    }

                    var logoMatch = System.Text.RegularExpressions.Regex.Match(content, @"logoFile\s*=\s*""([^""]+)""");
                    if (logoMatch.Success)
                        iconName = logoMatch.Groups[1].Value;
                }
            }

            // 4. Legacy Forge mod (mcmod.info)
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(modId))
            {
                var mcmodEntry = archive.GetEntry("mcmod.info");
                if (mcmodEntry is not null)
                {
                    using var stream = mcmodEntry.Open();
                    using var doc = System.Text.Json.JsonDocument.Parse(stream);
                    System.Text.Json.JsonElement firstMod;
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        firstMod = doc.RootElement[0];
                    else
                        firstMod = doc.RootElement;

                    if (firstMod.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (string.IsNullOrEmpty(title) &&
                            firstMod.TryGetProperty("name", out var nameProp) &&
                            nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            title = nameProp.GetString();
                        if (string.IsNullOrEmpty(modId) &&
                            firstMod.TryGetProperty("modid", out var midProp) &&
                            midProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            modId = midProp.GetString();
                        if (firstMod.TryGetProperty("logoFile", out var logoProp) && logoProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            iconName = logoProp.GetString();
                    }
                }
            }

            // Extract icon file if found
            string? localIconPath = null;
            if (!string.IsNullOrEmpty(iconName))
            {
                var iconEntry = archive.GetEntry(iconName.TrimStart('/'));
                if (iconEntry is not null)
                {
                    try
                    {
                        var cacheDir = Path.Combine(GamePaths.GetLauncherDirectory(), "cache", "mod_icons");
                        Directory.CreateDirectory(cacheDir);
                        var ext = Path.GetExtension(iconName);
                        if (string.IsNullOrEmpty(ext)) ext = ".png";
                        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(jarPath))).Substring(0, 12);
                        localIconPath = Path.Combine(cacheDir, $"{hash}{ext}");
                        if (!File.Exists(localIconPath))
                        {
                            using var inStream = iconEntry.Open();
                            using var outStream = File.Create(localIconPath);
                            inStream.CopyTo(outStream);
                        }
                    }
                    catch
                    {
                        // ignore icon extraction errors
                    }
                }
            }

            return (title, localIconPath, modId);
        }
        catch
        {
            // Ignore zip read errors
        }

        return (null, null, null);
    }

    private static List<ResourceItem> ScanFolders(string folderPath)
    {
        var list = new List<ResourceItem>();
        if (!Directory.Exists(folderPath))
            return list;

        try
        {
            var dirs = Directory.GetDirectories(folderPath)
                .Select(p => new DirectoryInfo(p))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var d in dirs)
            {
                list.Add(new ResourceItem(
                    d.Name,
                    ResourceDrop.FormatSize(ResourceDrop.DirectorySize(d.FullName)),
                    d.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    d.FullName,
                    isWorldSave: true));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] ScanFolders error: {ex.Message}");
        }

        return list;
    }

    [RelayCommand]
    private void OpenFolder(string subFolder)
    {
        if (string.IsNullOrWhiteSpace(_versionId))
            return;

        try
        {
            var rootDir = GamePaths.GetVersionInstanceDirectory(_versionId, _settingsService.Load().GameDirectory);
            var targetDir = string.IsNullOrWhiteSpace(subFolder) ? rootDir : Path.Combine(rootDir, subFolder);
            Directory.CreateDirectory(targetDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] OpenFolder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SearchModsForInstance()
    {
        if (App.MainWindowInstance is not MainWindow mainWindow || string.IsNullOrWhiteSpace(_versionId))
            return;

        try
        {
            var downloadVm = App.Services.GetRequiredService<DownloadViewModel>();
            var loaderKind = VersionKindDetector.Detect(_versionId);
            var loaderSlug = loaderKind switch
            {
                VersionKind.Fabric => "4",
                VersionKind.Quilt => "8",
                VersionKind.Forge => "1",
                VersionKind.NeoForge => "16",
                _ => "0"
            };
            var baseMcVersion = VersionKindDetector.DetectBaseGameVersion(_versionId);
            downloadVm.BeginInstanceModSearch(baseMcVersion, loaderSlug);
            mainWindow.NavigateToDownload();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceSettingsViewModel] SearchModsForInstance error: {ex.Message}");
        }
    }
}

public partial class ResourceItem : ObservableObject
{
    public string FileName { get; }
    public string SizeText { get; }
    public string DateText { get; }
    public string FullPath { get; }
    public bool IsToggleable { get; }
    public bool IsWorldSave { get; }
    public CatalogProjectKind ProjectKind { get; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string? _iconUrl;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool _isEnabled;

    public Uri? IconUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconUrl))
                return null;
            if (Uri.TryCreate(IconUrl, UriKind.Absolute, out var uri))
                return uri;
            try
            {
                if (Path.IsPathRooted(IconUrl))
                    return new Uri(Path.GetFullPath(IconUrl));
            }
            catch
            {
                // Ignore malformed local paths.
            }

            return null;
        }
    }
    public ImageSource? IconImage => CatalogIconCache.Get(IconUri, decodePixels: 64);

    public string ToggleGlyph => IsEnabled ? "\uE711" : "\uE73E"; // Click to Cancel vs Click to CheckMark
    public string ToggleToolTip => IsEnabled ? Loc.Get(LocKeys.InstanceSettings_ToggleDisable) : Loc.Get(LocKeys.InstanceSettings_ToggleEnable);
    public Brush ToggleForeground => IsEnabled
        ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        : (Brush)Application.Current.Resources["ArdelAccentBrush"];

    [ObservableProperty] private string? _webPageUrl;
    [ObservableProperty] private ModProjectItem? _matchedProject;
    [ObservableProperty] private bool _isSelected;

    public Action? SelectionChanged { get; set; }

    /// <summary>When true, IsSelected changes do not raise SelectionChanged (used during paint-drag).</summary>
    public bool SuppressSelectionNotify { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!SuppressSelectionNotify)
            SelectionChanged?.Invoke();
    }

    partial void OnIconUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(IconUri));
        OnPropertyChanged(nameof(IconImage));
    }

    [RelayCommand]
    private void ToggleEnable()
    {
        IsEnabled = !IsEnabled;
    }

    [RelayCommand]
    private async Task OpenPageAsync()
    {
        if (MatchedProject is not null && App.MainWindowInstance is MainWindow mainWindow)
        {
            try
            {
                var downloadVm = App.Services.GetRequiredService<DownloadViewModel>();
                var instance = InstanceSettingsViewModel.ActiveInstance;
                var gameVersion = instance is null
                    ? string.Empty
                    : VersionKindDetector.DetectBaseGameVersion(instance.VersionId);
                var loader = string.Empty;
                if (ProjectKind == CatalogProjectKind.Mod && instance is not null)
                {
                    loader = VersionKindDetector.Detect(instance.VersionId) switch
                    {
                        VersionKind.Fabric => "fabric",
                VersionKind.Quilt => "quilt",
                        VersionKind.Forge => "forge",
                        VersionKind.NeoForge => "neoforge",
                        _ => string.Empty
                    };
                }

                downloadVm.OpenCatalogProject(
                    MatchedProject,
                    ProjectKind,
                    new ModSearchHint(gameVersion, loader));
                mainWindow.NavigateToDownload();
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceItem] OpenPage internal nav error: {ex.Message}");
            }
        }

        // Fallback info dialog if not matched online
        try
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get(ProjectKind == CatalogProjectKind.Mod
                    ? LocKeys.InstanceSettings_ModInfoTitle
                    : LocKeys.InstanceSettings_PackInfoTitle),
                Content = $"{DisplayName}\n\n{Loc.Get(LocKeys.InstanceSettings_Name)}: {FileName}\n{Loc.Get(LocKeys.InstanceSettings_FileSize)}: {SizeText}\n{Loc.Get(LocKeys.InstanceSettings_Modified)}: {DateText}\n\n{(string.IsNullOrWhiteSpace(Description) ? Loc.Get(LocKeys.InstanceSettings_NoOnlineMatch) : Description)}",
                CloseButtonText = Loc.Get(LocKeys.Action_Close),
                XamlRoot = App.MainWindowInstance?.Content?.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceItem] Info dialog error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenItemFolder()
    {
        try
        {
            if (File.Exists(FullPath) || Directory.Exists(FullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{FullPath}\"");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceItem] OpenItemFolder error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenSavePropertiesAsync()
    {
        if (!IsWorldSave)
            return;

        var host = InstanceSettingsViewModel.ActiveInstance;
        if (host is null)
            return;

        await host.OpenSavePropertiesAsync(this).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteItemAsync()
    {
        try
        {
            if (!await ConfirmDialog.ConfirmDeleteAsync([DisplayName]).ConfigureAwait(true))
                return;

            if (File.Exists(FullPath) || Directory.Exists(FullPath))
            {
                RecycleBinHelper.MoveToRecycleBin(FullPath);
            }
            InstanceSettingsViewModel.ActiveInstance?.LoadResourceLists();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceItem] DeleteItem error: {ex.Message}");
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleGlyph));
        OnPropertyChanged(nameof(ToggleToolTip));
        OnPropertyChanged(nameof(ToggleForeground));

        if (!IsToggleable || _suppressFileToggle)
            return;

        ApplyEnabledState(value, reload: true);
    }

    /// <summary>Rename path ↔ path.disabled (file or folder). When <paramref name="reload"/> is false, batch callers refresh once.</summary>
    public void ApplyEnabledState(bool enabled, bool reload)
    {
        if (!IsToggleable)
            return;

        try
        {
            var isCurrentlyDisabled = FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (enabled && isCurrentlyDisabled)
            {
                var newPath = FullPath[..^".disabled".Length];
                MovePack(FullPath, newPath);
            }
            else if (!enabled && !isCurrentlyDisabled)
            {
                MovePack(FullPath, FullPath + ".disabled");
            }

            if (reload)
                InstanceSettingsViewModel.ActiveInstance?.LoadResourceLists();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceItem] Toggle failed for {FileName}: {ex.Message}");
        }
    }

    private static void MovePack(string from, string to)
    {
        if (File.Exists(to) || Directory.Exists(to))
            return;

        if (Directory.Exists(from))
            Directory.Move(from, to);
        else if (File.Exists(from))
            File.Move(from, to);
    }

    private bool _suppressFileToggle;

    public void SetEnabledForBatch(bool enabled)
    {
        _suppressFileToggle = true;
        try
        {
            IsEnabled = enabled;
            ApplyEnabledState(enabled, reload: false);
        }
        finally
        {
            _suppressFileToggle = false;
        }
    }

    public ResourceItem(
        string fileName,
        string sizeText,
        string dateText,
        string fullPath,
        bool isToggleable = false,
        CatalogProjectKind projectKind = CatalogProjectKind.Mod,
        bool isWorldSave = false)
    {
        FileName = fileName;
        SizeText = sizeText;
        DateText = dateText;
        FullPath = fullPath;
        IsToggleable = isToggleable;
        IsWorldSave = isWorldSave;
        ProjectKind = projectKind;
        _isEnabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        _displayName = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;
    }
}

/// <summary>A local mod jar that has a newer catalog file for this instance's MC + loader.</summary>
public partial class ModUpdateCandidate : ObservableObject
{
    public required ResourceItem LocalMod { get; init; }
    public required string DisplayName { get; init; }
    public required string CurrentFileName { get; init; }
    public required string NewFileName { get; init; }
    public required string CurrentVersionLabel { get; init; }
    public required string NewVersionLabel { get; init; }
    public required string DownloadUrl { get; init; }
    public string? IconUrl { get; init; }

    public Uri? IconUri => !string.IsNullOrEmpty(IconUrl) ? new Uri(IconUrl) : null;
    public ImageSource? IconImage => CatalogIconCache.Get(IconUri, decodePixels: 64);

    public string VersionChangeLabel =>
        Loc.Format(LocKeys.InstanceSettings_ModUpdateVersionChange, CurrentVersionLabel, NewVersionLabel);

    public Action? SelectionChanged { get; set; }

    [ObservableProperty] private bool _isSelected = true;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}
