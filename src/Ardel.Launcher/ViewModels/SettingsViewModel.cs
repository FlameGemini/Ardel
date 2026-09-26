using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LaunchViewModel _launchViewModel;
    private readonly SettingsService _settingsService;
    private readonly WeatherService _weather;
    private readonly Window _window;
    private bool _javaScanned;
    private bool _javaScanning;
    private bool _suppressLanguagePersist;
    private bool _suppressLanguageApply;
    private bool _suppressPersist;
    private bool _initialized;

    public SettingsViewModel(
        LaunchViewModel launchViewModel,
        SettingsService settingsService,
        WeatherService weather,
        Window window)
    {
        _launchViewModel = launchViewModel;
        _settingsService = settingsService;
        _weather = weather;
        _window = window;

        LanguageOptions = [];
        ThemeOptions = [];
        PopulateLocalizedOptions();
    }

    /// <summary>Loads persisted settings and launch bindings on first settings visit.</summary>
    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        SyncFromLaunch();
    }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }
    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public ObservableCollection<JavaInstallation> JavaInstallations => _launchViewModel.JavaInstallations;

    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private int _memoryMode;
    [ObservableProperty] private bool _showCustomMemoryControls = true;
    [ObservableProperty] private bool _useBmclApi;
    [ObservableProperty] private string _gameDirectory = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _javaVersionHint = string.Empty;
    /// <summary>"" = system, "en-US", "en-UK", "zh-CN", "zh-Hant", "ja-JP", "fr". Bound via ComboBox.SelectedValue.</summary>
    [ObservableProperty] private string _uiLanguageCode = string.Empty;
    [ObservableProperty] private string _appThemeCode = "Default";
    [ObservableProperty] private string _selectedThemeDescription = string.Empty;
    [ObservableProperty] private bool _isJavaBusy;
    [ObservableProperty] private bool _isJavaEmpty;
    [ObservableProperty] private bool _canEditJava = true;
    [ObservableProperty] private SettingsSection _selectedSection = SettingsSection.Defaults;
    [ObservableProperty] private DefaultsTopic _selectedDefaultsTopic = DefaultsTopic.None;
    [ObservableProperty] private PersonalizationTopic _selectedPersonalizationTopic = PersonalizationTopic.None;

    // Ardo
    [ObservableProperty] private int _ardoDownloadThreads = ArdoInstallOptions.DefaultDownloadThreads;
    [ObservableProperty] private int _ardoSpeedLimitKbps;
    [ObservableProperty] private int _ardoChunkSizeMb = ArdoInstallOptions.DefaultChunkSizeMb;
    [ObservableProperty] private bool _ardoVerifySha1 = true;
    [ObservableProperty] private int _ardoMaxRetries = ArdoInstallOptions.DefaultMaxRetries;

    // Behavior
    [ObservableProperty] private int _postLaunchWindowAction;
    [ObservableProperty] private int _bmclUsageMode = (int)Ardel.Launcher.Models.BmclUsageMode.FallbackWhenOfficialSlow;
    [ObservableProperty] private bool _openGameLogViewer;

    // Resource repair
    [ObservableProperty] private bool _resourceRepairEnabled = true;
    [ObservableProperty] private int _resourceRepairConcurrency = ArdoInstallOptions.DefaultCheckConcurrency;
    [ObservableProperty] private int _resourceVerifyMode;
    [ObservableProperty] private bool _resourceRepairAutoDownload = true;
    [ObservableProperty] private bool _resourceRepairWriteReadyMarker = true;
    [ObservableProperty] private bool _resourceRepairBlockLaunchOnFailure;

    // Crash analysis
    [ObservableProperty] private bool _crashAnalysisEnabled = true;
    [ObservableProperty] private bool _crashAnalysisOnForceKill;
    [ObservableProperty] private bool _crashAnalysisAutoOpenLogs;
    [ObservableProperty] private int _crashAnalysisKeepRecent = 10;
    [ObservableProperty] private bool _crashAnalysisPreIndexLogs;
    [ObservableProperty] private bool _crashAnalysisVerboseDebug;
    [ObservableProperty] private bool _crashAnalysisShowConfidence;

    // Startup splash
    [ObservableProperty] private bool _showStartupSplash = true;
    [ObservableProperty] private bool _startupSplashShowProgressBar = true;
    [ObservableProperty] private int _startupSplashDurationMs = StartupSplashOptions.DefaultDurationMs;
    [ObservableProperty] private bool _startupSplashShowBrandName = true;

    // Home quick launch
    [ObservableProperty] private bool _showHomeQuickLaunch = true;

    // Home widgets
    [ObservableProperty] private bool _showHomeWeather = true;
    [ObservableProperty] private bool _showHomeCalendar = true;
    [ObservableProperty] private string _weatherLocationQuery = string.Empty;
    [ObservableProperty] private string _weatherLocationDisplay = string.Empty;
    [ObservableProperty] private bool _weatherUseFahrenheit;
    [ObservableProperty] private bool _homeClockUse12Hour;
    private double? _weatherLatitude;
    private double? _weatherLongitude;
    private string? _weatherTimezone;

    public bool HasWeatherRegion =>
        _weatherLatitude is not null &&
        _weatherLongitude is not null &&
        !string.IsNullOrWhiteSpace(WeatherLocationDisplay);

    public string WeatherRegionSummary =>
        HasWeatherRegion
            ? WeatherLocationDisplay
            : Loc.Get(LocKeys.Settings_HomeWidgetsRegionNone);

    public bool ShowStartupSplashOptions => ShowStartupSplash;
    public bool ShowJavaHint => !IsJavaBusy && !IsJavaEmpty;
    public bool CanEditBmclUsage => UseBmclApi;
    public bool ShowBmclUsageDisabledHint => !UseBmclApi;

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public bool IsAppearanceSection => SelectedSection == SettingsSection.Appearance;

    public bool IsDefaultsSection => SelectedSection == SettingsSection.Defaults;

    public bool IsAboutSection => SelectedSection == SettingsSection.About;

    public bool IsPersonalizationHub =>
        IsAppearanceSection && SelectedPersonalizationTopic == PersonalizationTopic.None;

    public bool IsPersonalizationLanguage => SelectedPersonalizationTopic == PersonalizationTopic.Language;
    public bool IsPersonalizationTheme => SelectedPersonalizationTopic == PersonalizationTopic.Theme;
    public bool IsPersonalizationStartupSplash => SelectedPersonalizationTopic == PersonalizationTopic.StartupSplash;
    public bool IsPersonalizationQuickLaunch => SelectedPersonalizationTopic == PersonalizationTopic.QuickLaunch;
    public bool IsPersonalizationHomeWidgets => SelectedPersonalizationTopic == PersonalizationTopic.HomeWidgets;

    public bool IsDefaultsHub => IsDefaultsSection && SelectedDefaultsTopic == DefaultsTopic.None;

    public bool IsDefaultsJava => SelectedDefaultsTopic == DefaultsTopic.Java;
    public bool IsDefaultsMemory => SelectedDefaultsTopic == DefaultsTopic.Memory;
    public bool IsDefaultsMirror => SelectedDefaultsTopic == DefaultsTopic.Mirror;
    public bool IsDefaultsArdo => SelectedDefaultsTopic == DefaultsTopic.Ardo;
    public bool IsDefaultsBehavior => SelectedDefaultsTopic == DefaultsTopic.Behavior;
    public bool IsDefaultsResourceRepair => SelectedDefaultsTopic == DefaultsTopic.ResourceRepair;
    public bool IsDefaultsCrashAnalysis => SelectedDefaultsTopic == DefaultsTopic.CrashAnalysis;
    public bool IsDefaultsDialogDebug => SelectedDefaultsTopic == DefaultsTopic.DialogDebug;

    public string SelectedSectionTag => SelectedSection switch
    {
        SettingsSection.Defaults => "defaults",
        SettingsSection.About => "about",
        _ => "appearance"
    };

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public string SectionTitle => SelectedSection switch
    {
        SettingsSection.Defaults => Loc.Get(LocKeys.Settings_SectionDefaults),
        SettingsSection.About => Loc.Get(LocKeys.Settings_SectionAbout),
        _ => Loc.Get(LocKeys.Settings_SectionAppearance)
    };

    public string SectionDescription => SelectedSection switch
    {
        SettingsSection.Defaults => Loc.Get(LocKeys.Settings_SectionDefaultsDesc),
        SettingsSection.About => Loc.Get(LocKeys.Settings_SectionAboutDesc),
        _ => Loc.Get(LocKeys.Settings_SectionAppearanceDesc)
    };

    public string DefaultsDetailTitle => SelectedDefaultsTopic switch
    {
        DefaultsTopic.Java => Loc.Get(LocKeys.Settings_Java),
        DefaultsTopic.Memory => Loc.Get(LocKeys.Settings_MaxMemory),
        DefaultsTopic.Mirror => Loc.Get(LocKeys.Settings_BmclTitle),
        DefaultsTopic.Ardo => Loc.Get(LocKeys.Settings_ArdoTitle),
        DefaultsTopic.Behavior => Loc.Get(LocKeys.Settings_BehaviorTitle),
        DefaultsTopic.ResourceRepair => Loc.Get(LocKeys.Settings_ResourceRepairTitle),
        DefaultsTopic.CrashAnalysis => Loc.Get(LocKeys.Settings_CrashAnalysisTitle),
        DefaultsTopic.DialogDebug => Loc.Get(LocKeys.Settings_DialogDebugTitle),
        _ => string.Empty
    };

    public string PersonalizationDetailTitle => SelectedPersonalizationTopic switch
    {
        PersonalizationTopic.Language => Loc.Get(LocKeys.Settings_Language),
        PersonalizationTopic.Theme => Loc.Get(LocKeys.Settings_Theme),
        PersonalizationTopic.StartupSplash => Loc.Get(LocKeys.Settings_StartupSplash),
        PersonalizationTopic.QuickLaunch => Loc.Get(LocKeys.Settings_QuickLaunch),
        PersonalizationTopic.HomeWidgets => Loc.Get(LocKeys.Settings_HomeWidgets),
        _ => string.Empty
    };

    [ObservableProperty] private string _javaSummary = string.Empty;
    [ObservableProperty] private string _memorySummary = string.Empty;
    [ObservableProperty] private string _mirrorSummary = string.Empty;
    [ObservableProperty] private string _ardoSummary = string.Empty;
    [ObservableProperty] private string _behaviorSummary = string.Empty;
    [ObservableProperty] private string _resourceRepairSummary = string.Empty;
    [ObservableProperty] private string _crashAnalysisSummary = string.Empty;

    public bool IsMemoryCustom
    {
        get => MemoryMode != (int)DefaultMemoryMode.Dynamic;
        set
        {
            if (value)
                MemoryMode = (int)DefaultMemoryMode.Custom;
        }
    }

    public bool IsMemoryDynamic
    {
        get => MemoryMode == (int)DefaultMemoryMode.Dynamic;
        set
        {
            if (value)
                MemoryMode = (int)DefaultMemoryMode.Dynamic;
        }
    }

    public ThemeOption? SelectedTheme =>
        ThemeOptions.FirstOrDefault(t =>
            string.Equals(t.Code, AppThemeCode, StringComparison.OrdinalIgnoreCase));

    partial void OnIsJavaBusyChanged(bool value)
    {
        CanEditJava = !value;
        OnPropertyChanged(nameof(ShowJavaHint));
    }

    partial void OnIsJavaEmptyChanged(bool value) =>
        OnPropertyChanged(nameof(ShowJavaHint));

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        if (value != SettingsSection.Defaults)
            SelectedDefaultsTopic = DefaultsTopic.None;
        if (value != SettingsSection.Appearance)
            SelectedPersonalizationTopic = PersonalizationTopic.None;

        OnPropertyChanged(nameof(IsAppearanceSection));
        OnPropertyChanged(nameof(IsDefaultsSection));
        OnPropertyChanged(nameof(IsAboutSection));
        OnPropertyChanged(nameof(IsPersonalizationHub));
        OnPropertyChanged(nameof(IsDefaultsHub));
        OnPropertyChanged(nameof(SelectedSectionTag));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
    }

    partial void OnSelectedPersonalizationTopicChanged(PersonalizationTopic value)
    {
        OnPropertyChanged(nameof(IsPersonalizationHub));
        OnPropertyChanged(nameof(IsPersonalizationLanguage));
        OnPropertyChanged(nameof(IsPersonalizationTheme));
        OnPropertyChanged(nameof(IsPersonalizationStartupSplash));
        OnPropertyChanged(nameof(IsPersonalizationQuickLaunch));
        OnPropertyChanged(nameof(IsPersonalizationHomeWidgets));
        OnPropertyChanged(nameof(PersonalizationDetailTitle));
    }

    partial void OnSelectedDefaultsTopicChanged(DefaultsTopic value)
    {
        OnPropertyChanged(nameof(IsDefaultsHub));
        OnPropertyChanged(nameof(IsDefaultsJava));
        OnPropertyChanged(nameof(IsDefaultsMemory));
        OnPropertyChanged(nameof(IsDefaultsMirror));
        OnPropertyChanged(nameof(IsDefaultsArdo));
        OnPropertyChanged(nameof(IsDefaultsBehavior));
        OnPropertyChanged(nameof(IsDefaultsResourceRepair));
        OnPropertyChanged(nameof(IsDefaultsCrashAnalysis));
        OnPropertyChanged(nameof(IsDefaultsDialogDebug));
        OnPropertyChanged(nameof(DefaultsDetailTitle));

        if (value == DefaultsTopic.Java)
        {
            // Busy chrome before detail XAML Realize / java -version probes.
            if (!_javaScanned && !_javaScanning)
                IsJavaBusy = true;
            _ = EnsureJavaScannedCommand.ExecuteAsync(null);
        }
    }

    partial void OnJavaPathChanged(string? value)
    {
        _launchViewModel.JavaPath = value;
        _ = UpdateJavaHintAsync();
        PersistSettings();
    }

    partial void OnMaxRamMbChanged(int value)
    {
        _launchViewModel.MaxRamMb = value;
        PersistSettings();
        RefreshDefaultsSummaries();
    }

    partial void OnMemoryModeChanged(int value)
    {
        ShowCustomMemoryControls = value != (int)DefaultMemoryMode.Dynamic;
        OnPropertyChanged(nameof(IsMemoryCustom));
        OnPropertyChanged(nameof(IsMemoryDynamic));
        _launchViewModel.MemoryMode = value;
        PersistSettings();
        RefreshDefaultsSummaries();
    }

    partial void OnUseBmclApiChanged(bool value)
    {
        _launchViewModel.UseBmclApi = value;
        OnPropertyChanged(nameof(CanEditBmclUsage));
        OnPropertyChanged(nameof(ShowBmclUsageDisabledHint));
        PersistSettings();
        RefreshDefaultsSummaries();
    }

    partial void OnArdoDownloadThreadsChanged(int value) => PersistPreferenceSettings();
    partial void OnArdoSpeedLimitKbpsChanged(int value) => PersistPreferenceSettings();
    partial void OnArdoChunkSizeMbChanged(int value) => PersistPreferenceSettings();
    partial void OnArdoVerifySha1Changed(bool value) => PersistPreferenceSettings();
    partial void OnArdoMaxRetriesChanged(int value) => PersistPreferenceSettings();
    partial void OnPostLaunchWindowActionChanged(int value) => PersistPreferenceSettings();
    partial void OnBmclUsageModeChanged(int value) => PersistPreferenceSettings();
    partial void OnOpenGameLogViewerChanged(bool value) => PersistPreferenceSettings();
    partial void OnResourceRepairEnabledChanged(bool value) => PersistPreferenceSettings();
    partial void OnResourceRepairConcurrencyChanged(int value) => PersistPreferenceSettings();
    partial void OnResourceVerifyModeChanged(int value) => PersistPreferenceSettings();
    partial void OnResourceRepairAutoDownloadChanged(bool value) => PersistPreferenceSettings();
    partial void OnResourceRepairWriteReadyMarkerChanged(bool value) => PersistPreferenceSettings();
    partial void OnResourceRepairBlockLaunchOnFailureChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisEnabledChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisOnForceKillChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisAutoOpenLogsChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisKeepRecentChanged(int value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisPreIndexLogsChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisVerboseDebugChanged(bool value) => PersistPreferenceSettings();
    partial void OnCrashAnalysisShowConfidenceChanged(bool value) => PersistPreferenceSettings();
    partial void OnShowStartupSplashChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStartupSplashOptions));
        PersistPreferenceSettings();
    }
    partial void OnStartupSplashShowProgressBarChanged(bool value) => PersistPreferenceSettings();
    partial void OnStartupSplashDurationMsChanged(int value)
    {
        var clamped = Math.Clamp(value, StartupSplashOptions.MinDurationMs, StartupSplashOptions.MaxDurationMs);
        if (clamped != value)
        {
            StartupSplashDurationMs = clamped;
            return;
        }

        PersistPreferenceSettings();
    }
    partial void OnStartupSplashShowBrandNameChanged(bool value) => PersistPreferenceSettings();
    partial void OnShowHomeQuickLaunchChanged(bool value)
    {
        PersistPreferenceSettings();
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh();
    }
    partial void OnShowHomeWeatherChanged(bool value)
    {
        PersistPreferenceSettings();
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh(refreshWeather: true);
    }
    partial void OnShowHomeCalendarChanged(bool value)
    {
        PersistPreferenceSettings();
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh();
    }
    partial void OnWeatherUseFahrenheitChanged(bool value)
    {
        PersistPreferenceSettings();
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh(refreshWeather: true);
    }
    partial void OnHomeClockUse12HourChanged(bool value)
    {
        PersistPreferenceSettings();
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh();
    }

    [RelayCommand]
    private async Task ChooseWeatherRegionAsync()
    {
        if (_window.Content?.XamlRoot is not { } root)
            return;

        var picked = await Views.WeatherRegionDialog
            .ShowAsync(root, _weather, WeatherLocationQuery)
            .ConfigureAwait(true);
        if (picked is null)
            return;

        WeatherLocationDisplay = picked.DisplayName;
        WeatherLocationQuery = picked.Name;
        _weatherLatitude = picked.Latitude;
        _weatherLongitude = picked.Longitude;
        _weatherTimezone = picked.Timezone;
        PersistSettings();
        StatusText = Loc.Format(LocKeys.Settings_HomeWidgetsRegionSet, picked.DisplayName);
        OnPropertyChanged(nameof(HasWeatherRegion));
        OnPropertyChanged(nameof(WeatherRegionSummary));
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh(refreshWeather: true);
    }

    [RelayCommand]
    private void ClearWeatherRegion()
    {
        WeatherLocationDisplay = string.Empty;
        WeatherLocationQuery = string.Empty;
        _weatherLatitude = null;
        _weatherLongitude = null;
        _weatherTimezone = null;
        PersistSettings();
        OnPropertyChanged(nameof(HasWeatherRegion));
        OnPropertyChanged(nameof(WeatherRegionSummary));
        App.Services.GetService<HomeViewModel>()?.ApplyPreferenceRefresh(refreshWeather: true);
    }

    private void PersistPreferenceSettings()
    {
        if (_suppressPersist)
            return;
        PersistSettings();
        RefreshDefaultsSummaries();
    }

    partial void OnUiLanguageCodeChanged(string value)
    {
        if (_suppressLanguagePersist)
            return;

        try
        {
            var pref = value ?? string.Empty;
            var settings = _launchViewModel.SnapshotSettingsWithoutFlush();
            var previous = settings.UiLanguage ?? string.Empty;
            if (!string.Equals(previous, pref, StringComparison.Ordinal))
            {
                settings.UiLanguage = pref;
                _settingsService.Save(settings);
            }

            if (_suppressLanguageApply)
                return;

            var nextTag = App.ResolveLanguageTag(pref);
            if (string.Equals(nextTag, Loc.ActiveLanguageTag, StringComparison.OrdinalIgnoreCase))
            {
                RefreshLanguageSelectionStates();
                return;
            }

            var preference = pref;
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                () =>
                {
                    RefreshLanguageSelectionStates();
                    App.RelocalizeShell(preference);
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    partial void OnAppThemeCodeChanged(string value)
    {
        if (_suppressPersist)
        {
            SelectedThemeDescription = SelectedTheme?.Description ?? string.Empty;
            RefreshThemeSelectionStates();
            OnPropertyChanged(nameof(SelectedTheme));
            return;
        }

        try
        {
            var settings = _launchViewModel.SnapshotSettingsWithoutFlush();
            settings.AppTheme = value ?? "Default";
            settings.UiLanguage = UiLanguageCode ?? settings.UiLanguage ?? string.Empty;
            _settingsService.Save(settings);
            App.ApplyTheme(value);

            var selectedOption = ThemeOptions.FirstOrDefault(t => t.Code == value);
            SelectedThemeDescription = selectedOption?.Description ?? string.Empty;
            RefreshThemeSelectionStates();
            OnPropertyChanged(nameof(SelectedTheme));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    private async Task EnsureJavaScannedAsync()
    {
        if (_javaScanned || _javaScanning)
        {
            if (_javaScanned)
                IsJavaBusy = false;
            return;
        }

        await RescanJavaAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RescanJavaAsync()
    {
        if (_javaScanning)
            return;

        _javaScanning = true;
        IsJavaBusy = true;
        IsJavaEmpty = false;
        try
        {
            var javas = await Task.Run(JavaLocator.FindInstallations).ConfigureAwait(true);

            _launchViewModel.JavaInstallations.Clear();
            foreach (var java in javas)
                _launchViewModel.JavaInstallations.Add(java);

            JavaPath = _launchViewModel.JavaPath;
            StatusText = string.Empty;
            await UpdateJavaHintAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_ScanFailed, ex.Message);
            Debug.WriteLine(ex);
        }
        finally
        {
            _javaScanning = false;
            _javaScanned = true;
            IsJavaBusy = false;
            RefreshJavaEmpty();
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

            JavaPath = file.Path;
            StatusText = string.Empty;
            RefreshJavaEmpty();
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_BrowseFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    private void OpenGameDirectory()
    {
        try
        {
            GameDirectory = GamePaths.GetMinecraftRoot();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = GameDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_CannotOpenFolder, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDefaultsTopic(string? topic)
    {
        SelectedDefaultsTopic = topic?.ToLowerInvariant() switch
        {
            "java" => DefaultsTopic.Java,
            "memory" => DefaultsTopic.Memory,
            "mirror" => DefaultsTopic.Mirror,
            "ardo" => DefaultsTopic.Ardo,
            "behavior" => DefaultsTopic.Behavior,
            "resource" or "resourcerepair" => DefaultsTopic.ResourceRepair,
            "crash" or "crashanalysis" => DefaultsTopic.CrashAnalysis,
            "debug" or "dialogs" or "dialogdebug" => DefaultsTopic.DialogDebug,
            _ => DefaultsTopic.None
        };
    }

    [RelayCommand]
    private void CloseDefaultsTopic()
    {
        // Dialog debug is nested under Crash analysis — back returns there, not the hub.
        SelectedDefaultsTopic = SelectedDefaultsTopic == DefaultsTopic.DialogDebug
            ? DefaultsTopic.CrashAnalysis
            : DefaultsTopic.None;
    }

    [RelayCommand]
    private void OpenPersonalizationTopic(string? topic)
    {
        SelectedPersonalizationTopic = topic?.ToLowerInvariant() switch
        {
            "language" => PersonalizationTopic.Language,
            "theme" => PersonalizationTopic.Theme,
            "splash" or "startupsplash" => PersonalizationTopic.StartupSplash,
            "quicklaunch" or "quick" => PersonalizationTopic.QuickLaunch,
            "homewidgets" or "widgets" or "home" => PersonalizationTopic.HomeWidgets,
            _ => PersonalizationTopic.None
        };
    }

    [RelayCommand]
    private void ClosePersonalizationTopic() => SelectedPersonalizationTopic = PersonalizationTopic.None;

    [RelayCommand]
    private void AdjustStartupSplashDuration(string? direction)
    {
        var step = string.Equals(direction, "down", StringComparison.OrdinalIgnoreCase)
            ? -StartupSplashOptions.DurationStepMs
            : StartupSplashOptions.DurationStepMs;
        StartupSplashDurationMs = Math.Clamp(
            StartupSplashDurationMs + step,
            StartupSplashOptions.MinDurationMs,
            StartupSplashOptions.MaxDurationMs);
    }

    [RelayCommand]
    private async Task PreviewStartupSplashAsync()
    {
        if (_window is not MainWindow mainWindow)
            return;

        var options = BuildStartupSplashOptions();
        if (!options.Enabled)
        {
            StatusText = Loc.Get(LocKeys.Settings_StartupSplashPreviewDisabled);
            return;
        }

        try
        {
            await StartupSplash.PreviewAsync(mainWindow, AppThemeCode, options);
            StatusText = Loc.Get(LocKeys.Settings_StartupSplashPreviewDone);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    private async Task RerunSetupWizardAsync()
    {
        if (_window is not MainWindow mainWindow)
            return;

        await OobeHost.ReplayAsync(mainWindow, App.Services).ConfigureAwait(true);

        // OOBE writes disk + applies theme/language; pull those into this VM before
        // RelocalizeShell — otherwise Relocalize would keep the pre-wizard chips.
        try
        {
            SyncFromLaunch();
            var language = _settingsService.Load().UiLanguage;
            App.RelocalizeShell(language);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Relocalize after OOBE replay failed: {ex.Message}");
        }
    }

    private StartupSplashOptions BuildStartupSplashOptions() => new()
    {
        Enabled = ShowStartupSplash,
        ShowProgressBar = StartupSplashShowProgressBar,
        ShowBrandName = StartupSplashShowBrandName,
        DurationMs = Math.Clamp(
            StartupSplashDurationMs,
            StartupSplashOptions.MinDurationMs,
            StartupSplashOptions.MaxDurationMs)
    };

    private void PersistSettings()
    {
        if (_suppressPersist)
            return;

        try
        {
            var settings = _launchViewModel.SnapshotSettingsWithoutFlush();
            settings.GameDirectory = GamePaths.GetMinecraftRoot();
            settings.ForceVersionIsolation = true;
            settings.UiLanguage = UiLanguageCode ?? string.Empty;
            settings.AppTheme = AppThemeCode ?? "Default";
            ApplyUiPreferencesTo(settings);
            _settingsService.Save(settings);
            GameDirectory = settings.GameDirectory;
            RefreshDefaultsSummaries();
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    private void ApplyUiPreferencesTo(LauncherSettings settings)
    {
        settings.ArdoDownloadThreads = Math.Clamp(ArdoDownloadThreads, 1, 256);
        settings.ArdoSmallDownloadThreads = Math.Max(settings.ArdoDownloadThreads, ArdoInstallOptions.DefaultSmallDownloadThreads);
        settings.ArdoSpeedLimitKbps = Math.Max(0, ArdoSpeedLimitKbps);
        settings.ArdoChunkSizeMb = Math.Clamp(ArdoChunkSizeMb, 1, 64);
        settings.ArdoVerifySha1 = ArdoVerifySha1;
        settings.ArdoMaxRetries = Math.Clamp(ArdoMaxRetries, 1, 32);
        settings.PostLaunchWindowAction = PostLaunchWindowAction;
        settings.BmclUsageMode = BmclUsageMode;
        settings.OpenGameLogViewer = OpenGameLogViewer;
        settings.ResourceRepairEnabled = ResourceRepairEnabled;
        settings.ResourceRepairConcurrency = Math.Clamp(ResourceRepairConcurrency, 1, 256);
        settings.ResourceVerifyMode = ResourceVerifyMode;
        settings.ResourceRepairAutoDownload = ResourceRepairAutoDownload;
        settings.ResourceRepairWriteReadyMarker = ResourceRepairWriteReadyMarker;
        settings.ResourceRepairBlockLaunchOnFailure = ResourceRepairBlockLaunchOnFailure;
        settings.CrashAnalysisEnabled = CrashAnalysisEnabled;
        settings.CrashAnalysisOnForceKill = CrashAnalysisOnForceKill;
        settings.CrashAnalysisAutoOpenLogs = CrashAnalysisAutoOpenLogs;
        settings.CrashAnalysisKeepRecent = Math.Clamp(CrashAnalysisKeepRecent, 1, 50);
        settings.CrashAnalysisPreIndexLogs = CrashAnalysisPreIndexLogs;
        settings.CrashAnalysisVerboseDebug = CrashAnalysisVerboseDebug;
        settings.CrashAnalysisShowConfidence = CrashAnalysisShowConfidence;
        settings.ShowStartupSplash = ShowStartupSplash;
        settings.StartupSplashShowProgressBar = StartupSplashShowProgressBar;
        settings.StartupSplashDurationMs = Math.Clamp(
            StartupSplashDurationMs,
            StartupSplashOptions.MinDurationMs,
            StartupSplashOptions.MaxDurationMs);
        settings.StartupSplashShowBrandName = StartupSplashShowBrandName;
        settings.ShowHomeQuickLaunch = ShowHomeQuickLaunch;
        settings.ShowHomeWeather = ShowHomeWeather;
        settings.ShowHomeCalendar = ShowHomeCalendar;
        settings.WeatherLocationQuery = WeatherLocationQuery?.Trim() ?? string.Empty;
        settings.WeatherLocationDisplay = WeatherLocationDisplay?.Trim() ?? string.Empty;
        settings.WeatherUseFahrenheit = WeatherUseFahrenheit;
        settings.HomeClockUse12Hour = HomeClockUse12Hour;
        settings.WeatherLatitude = _weatherLatitude;
        settings.WeatherLongitude = _weatherLongitude;
        settings.WeatherTimezone = _weatherTimezone?.Trim() ?? string.Empty;
    }

    public void SyncFromLaunch()
    {
        ApplyLaunchBinding();
        ApplySettingsFromStore();
    }

    /// <summary>Refresh fields that mirror <see cref="LaunchViewModel"/> when returning to settings.</summary>
    public void RefreshLaunchBinding()
    {
        ApplyLaunchBinding();
    }

    private void ApplyLaunchBinding()
    {
        _suppressPersist = true;
        JavaPath = _launchViewModel.JavaPath;
        MaxRamMb = _launchViewModel.MaxRamMb;
        MemoryMode = _launchViewModel.MemoryMode == (int)DefaultMemoryMode.Dynamic
            ? (int)DefaultMemoryMode.Dynamic
            : (int)DefaultMemoryMode.Custom;
        ShowCustomMemoryControls = MemoryMode != (int)DefaultMemoryMode.Dynamic;
        UseBmclApi = _launchViewModel.UseBmclApi;
        GameDirectory = GamePaths.GetMinecraftRoot();
        _suppressPersist = false;

        _ = UpdateJavaHintAsync();
        RefreshJavaEmpty();
        RefreshDefaultsSummaries();
    }

    private void ApplySettingsFromStore()
    {
        _suppressPersist = true;
        var settings = _settingsService.Load();
        ArdoDownloadThreads = settings.ArdoDownloadThreads;
        ArdoSpeedLimitKbps = settings.ArdoSpeedLimitKbps;
        ArdoChunkSizeMb = settings.ArdoChunkSizeMb;
        ArdoVerifySha1 = settings.ArdoVerifySha1;
        ArdoMaxRetries = settings.ArdoMaxRetries;
        PostLaunchWindowAction = settings.PostLaunchWindowAction;
        BmclUsageMode = settings.BmclUsageMode;
        OpenGameLogViewer = settings.OpenGameLogViewer;
        ResourceRepairEnabled = settings.ResourceRepairEnabled;
        ResourceRepairConcurrency = settings.ResourceRepairConcurrency;
        ResourceVerifyMode = settings.ResourceVerifyMode;
        ResourceRepairAutoDownload = settings.ResourceRepairAutoDownload;
        ResourceRepairWriteReadyMarker = settings.ResourceRepairWriteReadyMarker;
        ResourceRepairBlockLaunchOnFailure = settings.ResourceRepairBlockLaunchOnFailure;
        CrashAnalysisEnabled = settings.CrashAnalysisEnabled;
        CrashAnalysisOnForceKill = settings.CrashAnalysisOnForceKill;
        CrashAnalysisAutoOpenLogs = settings.CrashAnalysisAutoOpenLogs;
        CrashAnalysisKeepRecent = settings.CrashAnalysisKeepRecent;
        CrashAnalysisPreIndexLogs = settings.CrashAnalysisPreIndexLogs;
        CrashAnalysisVerboseDebug = settings.CrashAnalysisVerboseDebug;
        CrashAnalysisShowConfidence = settings.CrashAnalysisShowConfidence;
        ShowStartupSplash = settings.ShowStartupSplash;
        StartupSplashShowProgressBar = settings.StartupSplashShowProgressBar;
        StartupSplashDurationMs = Math.Clamp(
            settings.StartupSplashDurationMs,
            StartupSplashOptions.MinDurationMs,
            StartupSplashOptions.MaxDurationMs);
        StartupSplashShowBrandName = settings.StartupSplashShowBrandName;
        ShowHomeQuickLaunch = settings.ShowHomeQuickLaunch;
        ShowHomeWeather = settings.ShowHomeWeather;
        ShowHomeCalendar = settings.ShowHomeCalendar;
        WeatherLocationQuery = settings.WeatherLocationQuery ?? string.Empty;
        WeatherLocationDisplay = settings.WeatherLocationDisplay ?? string.Empty;
        WeatherUseFahrenheit = settings.WeatherUseFahrenheit;
        HomeClockUse12Hour = settings.HomeClockUse12Hour;
        _weatherLatitude = settings.WeatherLatitude;
        _weatherLongitude = settings.WeatherLongitude;
        _weatherTimezone = string.IsNullOrWhiteSpace(settings.WeatherTimezone)
            ? null
            : settings.WeatherTimezone.Trim();
        OnPropertyChanged(nameof(HasWeatherRegion));
        OnPropertyChanged(nameof(WeatherRegionSummary));
        OnPropertyChanged(nameof(CanEditBmclUsage));
        OnPropertyChanged(nameof(ShowStartupSplashOptions));

        var saved = settings.UiLanguage ?? string.Empty;
        _suppressLanguagePersist = true;
        _suppressLanguageApply = true;
        UiLanguageCode = saved;
        _suppressLanguagePersist = false;
        _suppressLanguageApply = false;

        AppThemeCode = settings.AppTheme ?? "Default";
        SelectedThemeDescription = SelectedTheme?.Description ?? string.Empty;
        OnPropertyChanged(nameof(SelectedTheme));
        RefreshLanguageSelectionStates();
        RefreshThemeSelectionStates();
        StatusText = string.Empty;

        _suppressPersist = false;
    }

    /// <summary>Refresh option labels after <see cref="Loc.SetLanguage"/>.</summary>
    public void Relocalize()
    {
        if (!_initialized)
            return;

        // Prefer the store for language/theme — OOBE and other writers update disk first.
        // Rebuilding LanguageOptions clears ComboBox.SelectedValue and would re-enter
        // OnUiLanguageCodeChanged → RelocalizeShell (language thrash / blank settings).
        var settings = _settingsService.Load();
        var language = settings.UiLanguage ?? string.Empty;
        var theme = settings.AppTheme ?? "Default";
        _suppressPersist = true;
        _suppressLanguagePersist = true;
        _suppressLanguageApply = true;
        try
        {
            UiLanguageCode = language;
            AppThemeCode = theme;
            PopulateLocalizedOptions();
        }
        finally
        {
            _suppressLanguagePersist = false;
            _suppressLanguageApply = false;
            _suppressPersist = false;
        }

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        OnPropertyChanged(nameof(DefaultsDetailTitle));
        RefreshThemeSelectionStates();
        RefreshLanguageSelectionStates();
        SelectedThemeDescription = SelectedTheme?.Description ?? string.Empty;
        OnPropertyChanged(nameof(SelectedTheme));
        RefreshDefaultsSummaries();
    }

    /// <summary>Pull language/theme after OOBE finishes (no-op until settings was opened).</summary>
    public void ReloadAfterOobe()
    {
        if (!_initialized)
            return;

        SyncFromLaunch();
    }

    private void PopulateLocalizedOptions()
    {
        PersonalizationOptions.FillLanguageOptions(LanguageOptions, UiLanguageCode);
        PersonalizationOptions.FillThemeOptions(ThemeOptions, AppThemeCode);
    }

    private void RefreshLanguageSelectionStates()
    {
        var active = UiLanguageCode ?? string.Empty;
        for (var i = 0; i < LanguageOptions.Count; i++)
        {
            var option = LanguageOptions[i];
            var selected = string.Equals(option.Code, active, StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected == selected)
                continue;

            LanguageOptions[i] = option with { IsSelected = selected };
        }
    }

    private void RefreshThemeSelectionStates()
    {
        for (var i = 0; i < ThemeOptions.Count; i++)
        {
            var option = ThemeOptions[i];
            var selected = string.Equals(option.Code, AppThemeCode, StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected == selected)
                continue;

            ThemeOptions[i] = option with { IsSelected = selected };
        }
    }

    private void RefreshJavaEmpty()
    {
        IsJavaEmpty = _javaScanned && !_javaScanning && JavaInstallations.Count == 0;
    }

    private async Task UpdateJavaHintAsync()
    {
        var path = JavaPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            JavaVersionHint = Loc.Get(LocKeys.Settings_JavaAuto);
            RefreshDefaultsSummaries();
            return;
        }

        try
        {
            var major = await Task.Run(() => JavaLocator.GetJavaVersion(path)).ConfigureAwait(true);
            JavaVersionHint = Loc.Format(LocKeys.Settings_JavaSelected, major);
        }
        catch (Exception ex)
        {
            JavaVersionHint = ex.Message;
        }

        RefreshDefaultsSummaries();
    }

    private void RefreshDefaultsSummaries()
    {
        JavaSummary = string.IsNullOrWhiteSpace(JavaVersionHint)
            ? Loc.Get(LocKeys.Settings_JavaAuto)
            : JavaVersionHint;

        MemorySummary = MemoryMode == (int)DefaultMemoryMode.Dynamic
            ? Loc.Get(LocKeys.Memory_Dynamic)
            : $"{MaxRamMb} {Loc.Get(LocKeys.Settings_MemoryUnitMb)}";

        MirrorSummary = UseBmclApi
            ? Loc.Get(LocKeys.Settings_On)
            : Loc.Get(LocKeys.Settings_Off);

        var speed = ArdoSpeedLimitKbps <= 0
            ? Loc.Get(LocKeys.Settings_ArdoUnlimited)
            : $"{ArdoSpeedLimitKbps} KB/s";
        ArdoSummary = Loc.Format(LocKeys.Settings_ArdoSummary, ArdoDownloadThreads, speed);

        BehaviorSummary = PostLaunchWindowAction switch
        {
            (int)Ardel.Launcher.Models.PostLaunchWindowAction.Minimize => Loc.Get(LocKeys.Settings_PostLaunchMinimize),
            (int)Ardel.Launcher.Models.PostLaunchWindowAction.MinimizeUntilExit => Loc.Get(LocKeys.Settings_PostLaunchMinimizeUntilExit),
            (int)Ardel.Launcher.Models.PostLaunchWindowAction.ExitLauncher => Loc.Get(LocKeys.Settings_PostLaunchExit),
            _ => Loc.Get(LocKeys.Settings_PostLaunchNone)
        };

        ResourceRepairSummary = ResourceRepairEnabled
            ? Loc.Get(LocKeys.Settings_On)
            : Loc.Get(LocKeys.Settings_Off);

        CrashAnalysisSummary = CrashAnalysisEnabled
            ? Loc.Get(LocKeys.Settings_On)
            : Loc.Get(LocKeys.Settings_Off);
    }
}

public sealed record LanguageOption(string Code, string Label, bool IsSelected = false)
{
    public override string ToString() => Label;

    public bool IsSystemLanguage => string.IsNullOrEmpty(Code);

    /// <summary>Fixed-width badge text (BCP 47 or <c>Hant</c>).</summary>
    public string CodeBadgeText =>
        string.Equals(Code, "zh-Hant", StringComparison.OrdinalIgnoreCase)
            ? "Hant"
            : (Code ?? string.Empty).ToUpperInvariant();

    public Microsoft.UI.Xaml.Visibility CodeBadgeVisibility =>
        IsSystemLanguage ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility ComputerIconVisibility =>
        IsSystemLanguage ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility SelectedBadgeVisibility =>
        IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Media.Brush CardBackground =>
        ResolveThemeBrush(IsSelected ? "ArdelSelectedBrush" : "CardBackgroundFillColorDefaultBrush");

    public Microsoft.UI.Xaml.Media.Brush CardBorderBrush =>
        ResolveThemeBrush(IsSelected ? "ArdelAccentBrush" : "CardStrokeColorDefaultBrush");

    public Thickness CardBorderThickness => IsSelected ? new Thickness(1.5) : new Thickness(1);

    public Microsoft.UI.Xaml.Media.Brush LabelBrush =>
        ResolveThemeBrush(IsSelected ? "ArdelAccentBrush" : "TextFillColorPrimaryBrush");

    private static Microsoft.UI.Xaml.Media.Brush ResolveThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) &&
            resource is Microsoft.UI.Xaml.Media.Brush brush)
            return brush;

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}

public sealed record ThemeOption(
    string Code,
    string Label,
    string Description,
    Windows.UI.Color ThemeColor,
    Windows.UI.Color CanvasColor,
    Windows.UI.Color RailColor,
    Windows.UI.Color ChromeColor,
    bool IsSelected = false)
{
    public override string ToString() => Label;
    public Microsoft.UI.Xaml.Media.Brush PreviewBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColor);
    public Microsoft.UI.Xaml.Media.Brush PreviewCanvasBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(CanvasColor);
    public Microsoft.UI.Xaml.Media.Brush PreviewRailBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(RailColor);
    public Microsoft.UI.Xaml.Media.Brush PreviewChromeBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(ChromeColor);
    public bool IsDefault => Code == "Default";
    public Microsoft.UI.Xaml.Visibility DefaultIconVisibility => IsDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ColorCircleVisibility => !IsDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility SelectedBadgeVisibility => IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public double ThemeLabelOpacity => IsSelected ? 1.0 : 0.82;

    public Microsoft.UI.Xaml.Media.Brush ThemeLabelBrush =>
        ResolveThemeBrush(IsSelected ? "ArdelAccentBrush" : "TextFillColorPrimaryBrush");

    public Microsoft.UI.Xaml.Media.Brush ThemeChipBackground =>
        ResolveThemeBrush(IsSelected ? "ArdelSelectedBrush" : "SubtleFillColorSecondaryBrush");

    public Microsoft.UI.Xaml.Media.Brush ThemeChipBorderBrush =>
        ResolveThemeBrush(IsSelected ? "ArdelAccentBrush" : "ControlStrokeColorDefaultBrush");

    public Thickness ThemeChipBorderThickness =>
        IsSelected ? new Thickness(1.5) : new Thickness(1);

    private static Microsoft.UI.Xaml.Media.Brush ResolveThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) &&
            resource is Microsoft.UI.Xaml.Media.Brush brush)
            return brush;

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}


public enum SettingsSection
{
    Appearance,
    Defaults,
    About
}

public enum DefaultsTopic
{
    None,
    Java,
    Memory,
    Mirror,
    Ardo,
    Behavior,
    ResourceRepair,
    CrashAnalysis,
    DialogDebug
}

public enum PersonalizationTopic
{
    None,
    Language,
    Theme,
    StartupSplash,
    QuickLaunch,
    HomeWidgets
}
