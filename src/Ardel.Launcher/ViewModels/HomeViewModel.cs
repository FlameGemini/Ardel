using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

/// <summary>Home — greeting, widgets, center hero (logo / 3D skin head), quick-launch pin.</summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly LaunchViewModel _launch;
    private readonly SettingsService _settingsService;
    private readonly AccountStore _accounts;
    private readonly SkinLibraryStore _skins;
    private readonly WeatherService _weather;
    private readonly InstanceStatsStore _statsStore;
    private int _versionsLoadGate;
    private int _heroGeneration;
    private int _weatherGeneration;
    private DispatcherQueueTimer? _clockTimer;
    private DispatcherQueueTimer? _weatherTimer;
    private DispatcherQueueTimer? _heroDebounceTimer;
    private int _lastGreetingHour = -1;
    private bool _clockUse12Hour;
    private bool _showHomeQuickLaunch = true;
    private bool _prefShowHomeWeather = true;
    private bool _prefShowHomeCalendar = true;
    private string? _quickLaunchVersionId;
    private double? _weatherLatitude;
    private double? _weatherLongitude;
    private string _weatherLocationDisplay = string.Empty;
    private string _weatherLocationQuery = string.Empty;
    private string? _weatherTimezone;
    private bool _weatherUseFahrenheit;
    private bool _homePrefsWarm;
    private WeatherSnapshot? _lastWeather;
    private bool _cachedLogoLight;
    private int _cachedLogoPx = -1;

    public HomeViewModel(
        LaunchViewModel launch,
        SettingsService settingsService,
        AccountStore accounts,
        SkinLibraryStore skins,
        WeatherService weather,
        InstanceStatsStore statsStore)
    {
        _launch = launch;
        _settingsService = settingsService;
        _accounts = accounts;
        _skins = skins;
        _weather = weather;
        _statsStore = statsStore;

        _launch.Versions.CollectionChanged += OnVersionsChanged;
        _launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LaunchViewModel.IsLaunching)
                or nameof(LaunchViewModel.IsLocalReady)
                or nameof(LaunchViewModel.HasSignedInAccount))
            {
                RefreshQuickLaunch();
                RefreshPlayerAccountInfo();
                LaunchQuickCommand.NotifyCanExecuteChanged();
                if (e.PropertyName == nameof(LaunchViewModel.HasSignedInAccount) && App.IsStartupComplete)
                    _ = RefreshHeroAsync();
            }
            if (e.PropertyName is nameof(LaunchViewModel.ProgressValue)
                or nameof(LaunchViewModel.IsIndeterminate)
                or nameof(LaunchViewModel.StatusText)
                or nameof(LaunchViewModel.IsLaunching))
            {
                OnPropertyChanged(nameof(LaunchProgressValue));
                OnPropertyChanged(nameof(IsLaunchIndeterminate));
                OnPropertyChanged(nameof(LaunchStatusText));
                OnPropertyChanged(nameof(IsLaunching));
            }
        };
        _accounts.Changed += (_, _) =>
        {
            EnqueueHeroRefresh();
            RefreshPlayerAccountInfo();
        };
        _skins.Changed += (_, _) => EnqueueHeroRefresh();
        App.ThemeChanged += OnAppThemeChanged;
        SyncHomePreferenceCache();
        TryRestoreWeatherCache();
        if (App.IsStartupComplete)
        {
            _ = SyncBrandLogoImageAsync();
            _ = RefreshHeroAsync();
            _ = RefreshWeatherWidgetAsync();
        }
        else
        {
            App.StartupCompleted += OnStartupCompleted;
            // Prefetch under splash — do not wait for MarkStartupComplete (can be 1–12s).
            _ = PrefetchHomeChromeAsync();
        }
    }

    private async Task PrefetchHomeChromeAsync()
    {
        try
        {
            await Task.WhenAll(RefreshHeroAsync(), RefreshWeatherWidgetAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Prefetch failed: {ex.Message}");
        }
    }

    private void OnStartupCompleted(object? sender, EventArgs e)
    {
        App.StartupCompleted -= OnStartupCompleted;
        SyncHomePreferenceCache();
        // Prefetch may already have filled UI; refresh only if still empty / stale.
        if (HeroHeadImage is null || ShowBrandLogo)
            _ = RefreshHeroAsync();
        if (_lastWeather is null)
            _ = RefreshWeatherWidgetAsync();
    }

    private void OnAppThemeChanged()
    {
        _cachedLogoPx = -1;
        if (ShowBrandLogo || HeroHeadImage is null)
            _ = SyncBrandLogoImageAsync(force: true);
    }

    private void EnqueueHeroRefresh()
    {
        var dq = App.MainWindowInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dq is null)
        {
            _ = RefreshHeroAsync();
            return;
        }

        if (_heroDebounceTimer is null)
        {
            _heroDebounceTimer = dq.CreateTimer();
            _heroDebounceTimer.IsRepeating = false;
            _heroDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
            _heroDebounceTimer.Tick += (_, _) => _ = RefreshHeroAsync();
        }

        _heroDebounceTimer.Stop();
        _heroDebounceTimer.Start();
    }

    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private string _quickLaunchInstanceLabel = string.Empty;
    [ObservableProperty] private bool _hasQuickLaunch;
    [ObservableProperty] private string _quickLaunchHint = string.Empty;
    [ObservableProperty] private bool _showQuickLaunchButton = true;
    [ObservableProperty] private bool _showBrandLogo = true;
    [ObservableProperty] private BitmapImage? _brandLogoImage;
    [ObservableProperty] private BitmapImage? _heroHeadImage;

    [ObservableProperty] private bool _showWeatherWidget;
    [ObservableProperty] private bool _showCalendarWidget;
    [ObservableProperty] private string _weatherGlyph = "\uE9CE";
    [ObservableProperty] private string _weatherTemperature = string.Empty;
    [ObservableProperty] private string _weatherCondition = string.Empty;
    [ObservableProperty] private string _weatherLocation = string.Empty;
    [ObservableProperty] private string _calendarDateLine = string.Empty;
    [ObservableProperty] private string _calendarWeekday = string.Empty;
    [ObservableProperty] private string _clockTime = string.Empty;

    [ObservableProperty] private GameVersionItem? _selectedInstance;
    [ObservableProperty] private bool _hasInstance;
    [ObservableProperty] private string _instanceTotalPlayTime = "--";
    [ObservableProperty] private string _instanceLaunchCount = "0";
    [ObservableProperty] private string _instanceLastPlayed = "--";
    [ObservableProperty] private string _playerDisplayName = string.Empty;
    [ObservableProperty] private string _playerAccountType = string.Empty;
    [ObservableProperty] private bool _isPlayerSignedIn;

    public LaunchViewModel Launch => _launch;
    public System.Collections.ObjectModel.ObservableCollection<GameVersionItem> InstalledInstances => _launch.Versions;

    public double LaunchProgressValue => _launch.ProgressValue;
    public bool IsLaunchIndeterminate => _launch.IsIndeterminate;
    public string LaunchStatusText => _launch.StatusText;
    public bool IsLaunching => _launch.IsLaunching;

    public string SelectedInstanceDisplayName => SelectedInstance?.DisplayName ?? Loc.Get(LocKeys.Instances_Empty);
    public string SelectedInstanceId => SelectedInstance?.Id ?? string.Empty;
    public string SelectedInstanceKindLabel => SelectedInstance?.KindLabel ?? string.Empty;
    public string SelectedInstanceJavaLabel => SelectedInstance?.JavaRequirementLabel ?? string.Empty;
    public string SelectedInstanceIconGlyph => SelectedInstance?.IconGlyph ?? "\uE7FC";
    public BitmapImage? SelectedInstanceIconImage => SelectedInstance?.IconImage;

    partial void OnSelectedInstanceChanged(GameVersionItem? value)
    {
        HasInstance = value is not null;
        if (value is not null)
        {
            if (_launch.SelectedVersion != value)
                _launch.SelectedVersion = value;
            RefreshInstanceStats(value.Id);
            OnPropertyChanged(nameof(LaunchButtonSubtitle));
            LaunchQuickCommand.NotifyCanExecuteChanged();
        }
        else
        {
            InstanceTotalPlayTime = "--";
            InstanceLaunchCount = "0";
            InstanceLastPlayed = "--";
            OnPropertyChanged(nameof(LaunchButtonSubtitle));
        }
        OnPropertyChanged(nameof(SelectedInstanceDisplayName));
        OnPropertyChanged(nameof(SelectedInstanceId));
        OnPropertyChanged(nameof(SelectedInstanceKindLabel));
        OnPropertyChanged(nameof(SelectedInstanceJavaLabel));
        OnPropertyChanged(nameof(SelectedInstanceIconGlyph));
        OnPropertyChanged(nameof(SelectedInstanceIconImage));
    }

    /// <summary>Instance name when pinned; otherwise the empty-pin hint — shown under the launch title inside the button.</summary>
    public string LaunchButtonSubtitle =>
        HasQuickLaunch
            ? QuickLaunchInstanceLabel
            : string.IsNullOrEmpty(QuickLaunchHint)
                ? QuickLaunchInstanceLabel
                : QuickLaunchHint;

    public void RefreshGreeting()
    {
        Greeting = Loc.Get(ResolveGreetingKey(DateTime.Now.Hour));
    }

    public void Refresh()
    {
        RefreshGreeting();
        SyncClockPreference();
        RefreshClock();
        RefreshCalendarWidget();
        RefreshQuickLaunch();
        RefreshPlayerAccountInfo();
        RefreshInstanceStats();
        LaunchQuickCommand.NotifyCanExecuteChanged();
        _ = RefreshWeatherWidgetAsync();
        _ = RefreshHeroAsync();
        _ = EnsureVersionsLoadedAsync();
    }

    /// <summary>Light refresh when returning to Home — skips hero re-render if already shown.</summary>
    public void RefreshOnNavigate()
    {
        SyncHomePreferenceCache();
        RefreshGreeting();
        SyncClockPreference();
        RefreshClock();
        RefreshCalendarWidget();
        RefreshQuickLaunch();
        RefreshPlayerAccountInfo();
        RefreshInstanceStats();
        LaunchQuickCommand.NotifyCanExecuteChanged();

        // Do not wait for splash MarkStartupComplete — Home is already under the overlay.
        if (ShowWeatherWidget && _lastWeather is null)
            _ = RefreshWeatherWidgetAsync();
        if (HeroHeadImage is null)
            _ = RefreshHeroAsync();

        _ = EnsureVersionsLoadedAsync();
    }

    public void Relocalize() => Refresh();

    /// <summary>Re-apply home widget prefs after Settings changes (clock format, weather region, etc.).</summary>
    public void ApplyPreferenceRefresh(bool refreshWeather = false)
    {
        SyncHomePreferenceCache();
        RefreshClock();
        RefreshCalendarWidget();
        RefreshQuickLaunch();
        LaunchQuickCommand.NotifyCanExecuteChanged();
        if (ShowWeatherWidget && (refreshWeather || _lastWeather is null))
            _ = RefreshWeatherWidgetAsync();
    }

    public void StartClock()
    {
        SyncClockPreference();
        RefreshClock();
        if (_clockTimer is null)
        {
            var dq = App.MainWindowInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            _clockTimer = dq.CreateTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.IsRepeating = true;
            _clockTimer.Tick += OnClockTick;
            _clockTimer.Start();
        }

        StartWeatherRefreshTimer();
    }

    private void StartWeatherRefreshTimer()
    {
        if (_weatherTimer is not null)
            return;

        var dq = App.MainWindowInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        _weatherTimer = dq.CreateTimer();
        _weatherTimer.Interval = TimeSpan.FromMinutes(15);
        _weatherTimer.IsRepeating = true;
        _weatherTimer.Tick += OnWeatherRefreshTick;
        _weatherTimer.Start();
    }

    private void OnWeatherRefreshTick(DispatcherQueueTimer sender, object args) =>
        _ = RefreshWeatherWidgetAsync();

    public void StopClock()
    {
        if (_clockTimer is null && _weatherTimer is null)
            return;
        if (_clockTimer is not null)
        {
            _clockTimer.Stop();
            _clockTimer.Tick -= OnClockTick;
            _clockTimer = null;
        }

        StopWeatherRefreshTimer();
    }

    private void StopWeatherRefreshTimer()
    {
        if (_weatherTimer is null)
            return;
        _weatherTimer.Stop();
        _weatherTimer.Tick -= OnWeatherRefreshTick;
        _weatherTimer = null;
    }

    private void OnClockTick(DispatcherQueueTimer sender, object args) => RefreshClock();

    public void RefreshClock()
    {
        var now = DateTime.Now;
        // Prefer the cached preference so the 1 Hz tick never touches SettingsService/disk.
        var next = _clockUse12Hour
            ? now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)
            : now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        // Skip redundant PropertyChanged every second — major source of "UI feels sticky".
        if (!string.Equals(ClockTime, next, StringComparison.Ordinal))
            ClockTime = next;

        if (now.Hour != _lastGreetingHour)
        {
            _lastGreetingHour = now.Hour;
            RefreshGreeting();
        }
    }

    private void SyncClockPreference(LauncherSettings? settings = null) =>
        SyncHomePreferenceCache(settings);

    private void SyncHomePreferenceCache(LauncherSettings? settings = null)
    {
        settings ??= _settingsService.Load();
        _clockUse12Hour = settings.HomeClockUse12Hour;
        _showHomeQuickLaunch = settings.ShowHomeQuickLaunch;
        _prefShowHomeWeather = settings.ShowHomeWeather;
        _prefShowHomeCalendar = settings.ShowHomeCalendar;
        ShowWeatherWidget = _prefShowHomeWeather;
        ShowCalendarWidget = _prefShowHomeCalendar;
        _quickLaunchVersionId = string.IsNullOrWhiteSpace(settings.QuickLaunchVersionId)
            ? null
            : settings.QuickLaunchVersionId.Trim();
        _weatherLatitude = settings.WeatherLatitude;
        _weatherLongitude = settings.WeatherLongitude;
        _weatherLocationDisplay = settings.WeatherLocationDisplay?.Trim() ?? string.Empty;
        _weatherLocationQuery = settings.WeatherLocationQuery?.Trim() ?? string.Empty;
        _weatherTimezone = string.IsNullOrWhiteSpace(settings.WeatherTimezone)
            ? null
            : settings.WeatherTimezone.Trim();
        _weatherUseFahrenheit = settings.WeatherUseFahrenheit;
        _homePrefsWarm = true;
    }

    public void RefreshCalendarWidget()
    {
        if (!_homePrefsWarm)
            SyncHomePreferenceCache();

        ShowCalendarWidget = _prefShowHomeCalendar;
        if (!ShowCalendarWidget)
        {
            CalendarDateLine = string.Empty;
            CalendarWeekday = string.Empty;
            return;
        }

        var now = DateTime.Now;
        var culture = ResolveUiCulture();
        CalendarDateLine = now.ToString("M", culture);
        CalendarWeekday = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
    }

    public async Task RefreshWeatherWidgetAsync()
    {
        var generation = Interlocked.Increment(ref _weatherGeneration);
        if (!_homePrefsWarm)
            SyncHomePreferenceCache();

        ShowWeatherWidget = _prefShowHomeWeather;
        if (!ShowWeatherWidget)
            return;

        WeatherLocation = _weatherLocationDisplay;

        if (_weatherLatitude is not { } lat ||
            _weatherLongitude is not { } lon)
        {
            _lastWeather = null;
            ApplyWeatherUi(() =>
            {
                WeatherGlyph = "\uE707";
                WeatherTemperature = string.Empty;
                WeatherCondition = Loc.Get(LocKeys.Home_WeatherNeedRegion);
            }, animate: false);
            return;
        }

        if (_lastWeather is not null)
            ApplyWeatherUi(() => ApplyWeatherSnapshot(_lastWeather), animate: false);
        else
        {
            ApplyWeatherUi(() =>
            {
                WeatherCondition = Loc.Get(LocKeys.Home_WeatherLoading);
                WeatherTemperature = string.Empty;
                WeatherGlyph = "\uE9CE";
            }, animate: false);
        }

        try
        {
            var snap = await _weather
                .GetCurrentAsync(
                    lat,
                    lon,
                    _weatherUseFahrenheit,
                    _weatherTimezone)
                .ConfigureAwait(false);
            if (generation != _weatherGeneration)
                return;

            // One soft retry — first attempt often races DNS / cold socket after splash.
            if (snap is null)
            {
                await Task.Delay(400).ConfigureAwait(false);
                if (generation != _weatherGeneration)
                    return;
                snap = await _weather
                    .GetCurrentAsync(
                        lat,
                        lon,
                        _weatherUseFahrenheit,
                        _weatherTimezone)
                    .ConfigureAwait(false);
                if (generation != _weatherGeneration)
                    return;
            }

            if (snap is null)
            {
                if (_lastWeather is null)
                    ApplyWeatherUi(ShowWeatherFailed, animate: true);
                return;
            }

            _lastWeather = snap;
            PersistWeatherCache(snap, lat, lon);
            // Pulse only loading→result; revisiting Home with cached weather must stay instant.
            var pulse = string.IsNullOrEmpty(WeatherTemperature);
            ApplyWeatherUi(() => ApplyWeatherSnapshot(snap), animate: pulse);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Weather failed: {ex.Message}");
            if (generation != _weatherGeneration)
                return;
            if (_lastWeather is not null)
            {
                ApplyWeatherUi(() => ApplyWeatherSnapshot(_lastWeather), animate: false);
                return;
            }

            ApplyWeatherUi(ShowWeatherFailed, animate: true);
        }
    }

    private void ApplyWeatherSnapshot(WeatherSnapshot snap)
    {
        WeatherTemperature = WeatherPresentation.FormatTemperature(snap.Temperature, snap.UseFahrenheit);
        WeatherCondition = WeatherPresentation.FormatConditionLine(
            WeatherPresentation.ConditionLabel(snap.WeatherCode, snap.IsDay),
            snap.DailyHigh,
            snap.DailyLow,
            snap.UseFahrenheit);
        WeatherGlyph = WeatherPresentation.ConditionGlyph(snap.WeatherCode, snap.IsDay);
        if (string.IsNullOrWhiteSpace(WeatherLocation))
            WeatherLocation = _weatherLocationQuery;
    }

    private static string WeatherCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ardel",
            "weather-cache.json");

    private void TryRestoreWeatherCache()
    {
        if (_lastWeather is not null)
            return;
        if (_weatherLatitude is not { } lat || _weatherLongitude is not { } lon)
            return;

        try
        {
            var path = WeatherCachePath;
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var cached = System.Text.Json.JsonSerializer.Deserialize<WeatherCacheDto>(json);
            if (cached is null)
                return;
            if (Math.Abs(cached.Latitude - lat) > 0.01 || Math.Abs(cached.Longitude - lon) > 0.01)
                return;
            if (cached.UseFahrenheit != _weatherUseFahrenheit)
                return;
            // Stale after 3 hours — still show briefly, network refresh will replace.
            if (cached.SavedUtc.AddHours(6) < DateTime.UtcNow)
                return;

            _lastWeather = new WeatherSnapshot(
                cached.Temperature,
                cached.WeatherCode,
                cached.IsDay,
                cached.UseFahrenheit,
                cached.Timezone,
                cached.DailyHigh,
                cached.DailyLow);

            WeatherLocation = string.IsNullOrWhiteSpace(_weatherLocationDisplay)
                ? _weatherLocationQuery
                : _weatherLocationDisplay;
            ShowWeatherWidget = _prefShowHomeWeather;
            if (ShowWeatherWidget)
                ApplyWeatherSnapshot(_lastWeather);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Weather cache restore failed: {ex.Message}");
        }
    }

    private void PersistWeatherCache(WeatherSnapshot snap, double lat, double lon)
    {
        try
        {
            var dir = Path.GetDirectoryName(WeatherCachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new WeatherCacheDto
            {
                Latitude = lat,
                Longitude = lon,
                Temperature = snap.Temperature,
                WeatherCode = snap.WeatherCode,
                IsDay = snap.IsDay,
                UseFahrenheit = snap.UseFahrenheit,
                Timezone = snap.Timezone,
                DailyHigh = snap.DailyHigh,
                DailyLow = snap.DailyLow,
                SavedUtc = DateTime.UtcNow
            };
            File.WriteAllText(
                WeatherCachePath,
                System.Text.Json.JsonSerializer.Serialize(dto));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Weather cache write failed: {ex.Message}");
        }
    }

    private sealed class WeatherCacheDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Temperature { get; set; }
        public int WeatherCode { get; set; }
        public bool IsDay { get; set; }
        public bool UseFahrenheit { get; set; }
        public string? Timezone { get; set; }
        public double? DailyHigh { get; set; }
        public double? DailyLow { get; set; }
        public DateTime SavedUtc { get; set; }
    }

    private void ShowWeatherFailed()
    {
        WeatherTemperature = string.Empty;
        WeatherCondition = Loc.Get(LocKeys.Home_WeatherFailed);
        WeatherGlyph = "\uE783";
    }

    /// <summary>Optional UI pulse (fade out → apply → fade in). Set by HomePage.</summary>
    public Func<Action, Task>? WeatherContentAnimator { get; set; }

    private void ApplyWeatherUi(Action action, bool animate = true)
    {
        var dq = App.MainWindowInstance?.DispatcherQueue
                 ?? DispatcherQueue.GetForCurrentThread();
        if (dq is null)
        {
            action();
            return;
        }

        void Run()
        {
            if (!animate || WeatherContentAnimator is null)
            {
                action();
                return;
            }

            _ = WeatherContentAnimator(action);
        }

        if (dq.HasThreadAccess)
        {
            Run();
            return;
        }

        if (!dq.TryEnqueue(Run))
            Debug.WriteLine("[Home] Weather UI enqueue failed.");
    }

    private static System.Globalization.CultureInfo ResolveUiCulture()
    {
        try
        {
            var tag = Loc.ActiveLanguageTag switch
            {
                var t when t.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) => "zh-TW",
                var t when t.StartsWith("zh", StringComparison.OrdinalIgnoreCase) => "zh-CN",
                var t when t.StartsWith("en-UK", StringComparison.OrdinalIgnoreCase) => "en-GB",
                var t when t.StartsWith("ja", StringComparison.OrdinalIgnoreCase) => "ja-JP",
                var t when t.StartsWith("fr", StringComparison.OrdinalIgnoreCase) => "fr-FR",
                var t when t.StartsWith("es", StringComparison.OrdinalIgnoreCase) => "es-ES",
                var t when t.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => "ko-KR",
                var t when t.StartsWith("de", StringComparison.OrdinalIgnoreCase) => "de-DE",
                var t when t.StartsWith("pt", StringComparison.OrdinalIgnoreCase) => "pt-BR",
                var t when t.StartsWith("it", StringComparison.OrdinalIgnoreCase) => "it-IT",
                var t when t.StartsWith("ru", StringComparison.OrdinalIgnoreCase) => "ru-RU",
                var t => t
            };
            return new System.Globalization.CultureInfo(tag);
        }
        catch
        {
            return System.Globalization.CultureInfo.CurrentUICulture;
        }
    }

    public async Task RefreshHeroAsync()
    {
        var generation = Interlocked.Increment(ref _heroGeneration);

        try
        {
            await _skins.EnsureReadyAsync().ConfigureAwait(true);
            if (generation != _heroGeneration)
                return;

            string? skinPath = null;
            string? accountUuid = null;

            var active = _accounts.GetActive();
            if (active is not null)
            {
                accountUuid = active.Uuid;
                var skin = _skins.Find(active.SkinId);
                if (skin is null && active.Kind == AccountKind.Microsoft && !string.IsNullOrWhiteSpace(active.Uuid))
                {
                    var clean = active.Uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
                    skin = _skins.Find($"official-{clean}");
                    if (skin is null)
                    {
                        try
                        {
                            var accountVm = App.Services.GetService<AccountViewModel>();
                            if (accountVm is not null)
                            {
                                await accountVm.EnsureMicrosoftSkinsAsync().ConfigureAwait(true);
                                active = _accounts.GetActive();
                                skin = _skins.Find(active?.SkinId) ?? _skins.Find($"official-{clean}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Home] EnsureMicrosoftSkinsAsync in RefreshHero failed: {ex.Message}");
                        }
                    }
                }

                if (skin is not null && (skin.IsBuiltIn || skin.IsConfigured))
                {
                    skinPath = _skins.GetAbsolutePath(skin);
                }
            }

            // Fall back to Steve if no custom skin is found
            if (string.IsNullOrWhiteSpace(skinPath) || !File.Exists(skinPath))
            {
                var builtInSteve = _skins.Find(SkinLibraryStore.BuiltinSteveOfflineId) ?? _skins.Find("steve");
                if (builtInSteve is not null)
                {
                    skinPath = _skins.GetAbsolutePath(builtInSteve);
                }

                if (string.IsNullOrWhiteSpace(skinPath) || !File.Exists(skinPath))
                {
                    var candidates = new[]
                    {
                        Path.Combine(_skins.RootDirectory, "steve.png"),
                        Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", "steve.png"),
                        Path.Combine(AppContext.BaseDirectory, "Skins", "steve.png"),
                        Path.Combine(GamePaths.GetLauncherDirectory(), "src", "Ardel.Launcher", "Assets", "Skins", "steve.png"),
                    };
                    skinPath = candidates.FirstOrDefault(File.Exists);
                }
            }

            if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
            {
                var image = await Skin3DHeadHelper.TryCreateAsync(skinPath).ConfigureAwait(true)
                            ?? await SkinPreviewHelper.TryCreateHeadPreviewAsync(skinPath, displaySize: 280).ConfigureAwait(true);
                if (generation == _heroGeneration && image is not null)
                {
                    HeroHeadImage = image;
                    ShowBrandLogo = false;
                }
            }
            else
            {
                if (generation == _heroGeneration)
                {
                    HeroHeadImage = null;
                    ShowBrandLogo = true;
                    await SyncBrandLogoImageAsync().ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Hero skin render failed: {ex.Message}");
            if (generation != _heroGeneration)
                return;
            HeroHeadImage = null;
            ShowBrandLogo = true;
            await SyncBrandLogoImageAsync().ConfigureAwait(true);
        }
    }

    private async Task SyncBrandLogoImageAsync(bool force = false)
    {
        try
        {
            var lightShell = !(App.MainWindowInstance is MainWindow mw
                ? AppThemeController.ResolveSystemIsDark(mw)
                : Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var scale = App.MainWindowInstance?.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            // 180 DIP × DPI × 2 — Skia AA at high res, Image downscales smoothly.
            var px = (int)Math.Clamp(Math.Ceiling(180 * scale * 2), 256, 1024);
            if (!force &&
                BrandLogoImage is not null &&
                _cachedLogoLight == lightShell &&
                _cachedLogoPx == px)
            {
                return;
            }

            BrandLogoImage = await ArdelLogoRenderer.CreateAsync(px, lightShell).ConfigureAwait(true);
            _cachedLogoLight = lightShell;
            _cachedLogoPx = px;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Brand logo render failed: {ex.Message}");
            var lightShell = Application.Current.RequestedTheme == ApplicationTheme.Light;
            BrandLogoImage = new BitmapImage(new Uri(lightShell
                ? "ms-appx:///Assets/ardel-logo-ink.png"
                : "ms-appx:///Assets/ardel-logo.png"));
            _cachedLogoLight = lightShell;
            _cachedLogoPx = -1;
        }
    }

    public void RefreshQuickLaunch()
    {
        if (!_homePrefsWarm)
            SyncHomePreferenceCache();

        ShowQuickLaunchButton = _showHomeQuickLaunch;

        var id = _quickLaunchVersionId;
        GameVersionItem? item = null;
        if (!string.IsNullOrEmpty(id))
        {
            item = _launch.Versions.FirstOrDefault(v =>
                string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (item is null)
        {
            HasQuickLaunch = false;
            QuickLaunchInstanceLabel = string.Empty;
            QuickLaunchHint = Loc.Get(LocKeys.Home_QuickLaunchNone);
            SelectedInstance = null;
            HasInstance = false;
            OnPropertyChanged(nameof(LaunchButtonSubtitle));
            return;
        }

        HasQuickLaunch = true;
        QuickLaunchInstanceLabel = item.Id;
        QuickLaunchHint = string.Empty;
        if (SelectedInstance != item)
        {
            SelectedInstance = item;
        }
        HasInstance = true;
        RefreshInstanceStats(item.Id);
        OnPropertyChanged(nameof(LaunchButtonSubtitle));
    }

    public void RefreshPlayerAccountInfo()
    {
        IsPlayerSignedIn = _launch.HasSignedInAccount;
        var active = _accounts.GetActive();
        if (active is not null && !string.IsNullOrWhiteSpace(active.DisplayName))
        {
            PlayerDisplayName = active.DisplayName;
            PlayerAccountType = active.Kind == AccountKind.Microsoft
                ? Loc.Get(LocKeys.Account_KindMicrosoft)
                : Loc.Get(LocKeys.Account_KindOffline);
        }
        else
        {
            PlayerDisplayName = Loc.Get(LocKeys.Brand_Name);
            PlayerAccountType = Loc.Get(LocKeys.Account_KindOffline);
        }
    }

    public void RefreshInstanceStats(string? versionId = null)
    {
        var targetId = versionId ?? SelectedInstance?.Id ?? _quickLaunchVersionId;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            InstanceTotalPlayTime = "--";
            InstanceLaunchCount = "0";
            InstanceLastPlayed = "--";
            return;
        }

        try
        {
            var data = _statsStore.Load(targetId, GamePaths.GetMinecraftRoot());
            if (data.TotalPlaySeconds > 0)
            {
                var hours = data.TotalPlaySeconds / 3600.0;
                InstanceTotalPlayTime = hours >= 1.0
                    ? $"{hours:F1} h"
                    : $"{Math.Max(1, data.TotalPlaySeconds / 60)} m";
            }
            else
            {
                InstanceTotalPlayTime = "--";
            }

            InstanceLaunchCount = data.LaunchCount > 0 ? $"{data.LaunchCount}" : "0";

            if (data.LastSessionStartUtc.HasValue)
            {
                var local = data.LastSessionStartUtc.Value.ToLocalTime();
                var span = DateTime.Now - local;
                if (span.TotalDays < 1)
                    InstanceLastPlayed = local.ToString("HH:mm");
                else if (span.TotalDays < 2)
                    InstanceLastPlayed = "Yesterday";
                else
                    InstanceLastPlayed = local.ToString("M/d");
            }
            else
            {
                InstanceLastPlayed = "--";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] RefreshInstanceStats failed: {ex.Message}");
            InstanceTotalPlayTime = "--";
            InstanceLaunchCount = "0";
            InstanceLastPlayed = "--";
        }
    }

    [RelayCommand]
    private void NavigateToAccount() => (App.MainWindowInstance as MainWindow)?.SelectNavTag("account");

    [RelayCommand]
    private void NavigateToDownload() => (App.MainWindowInstance as MainWindow)?.SelectNavTag("download");

    [RelayCommand]
    private void NavigateToMods()
    {
        try
        {
            var downloads = App.Services.GetService<DownloadViewModel>();
            if (downloads is not null)
                downloads.SelectedSection = DownloadSection.Mod;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] NavigateToMods failed: {ex.Message}");
        }
        (App.MainWindowInstance as MainWindow)?.SelectNavTag("download");
    }

    [RelayCommand]
    private void NavigateToInstances() => (App.MainWindowInstance as MainWindow)?.SelectNavTag("instances");

    [RelayCommand]
    private void OpenInstanceFolder()
    {
        var target = SelectedInstance?.Id ?? _quickLaunchVersionId;
        if (string.IsNullOrWhiteSpace(target))
            return;
        try
        {
            var path = GamePaths.GetVersionInstanceDirectory(target);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] OpenInstanceFolder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenScreenshots()
    {
        var target = SelectedInstance?.Id ?? _quickLaunchVersionId;
        try
        {
            var path = string.IsNullOrWhiteSpace(target)
                ? Path.Combine(GamePaths.GetMinecraftRoot(), "screenshots")
                : Path.Combine(GamePaths.GetVersionInstanceDirectory(target), "screenshots");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] OpenScreenshots failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenMinecraftFolder()
    {
        try
        {
            var path = GamePaths.GetMinecraftRoot();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] OpenMinecraftFolder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenInstanceSettings()
    {
        var target = SelectedInstance?.Id ?? _quickLaunchVersionId;
        if (!string.IsNullOrWhiteSpace(target))
            (App.MainWindowInstance as MainWindow)?.NavigateToInstanceSettings(target);
        else
            (App.MainWindowInstance as MainWindow)?.SelectNavTag("instances");
    }

    private async Task EnsureVersionsLoadedAsync()
    {
        if (_launch.IsLocalReady)
            return;
        if (Interlocked.Exchange(ref _versionsLoadGate, 1) == 1)
            return;

        try
        {
            await _launch.LoadLocalVersionsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Load versions failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _versionsLoadGate, 0);
            RefreshQuickLaunch();
            LaunchQuickCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLaunchQuick() =>
        !_launch.IsLaunching && _launch.IsLocalReady;

    [RelayCommand(CanExecute = nameof(CanLaunchQuick))]
    private async Task LaunchQuickAsync()
    {
        if (!_homePrefsWarm)
            SyncHomePreferenceCache();

        var id = _quickLaunchVersionId;
        if (string.IsNullOrEmpty(id) || SelectedInstance is null)
        {
            // If no quick launch instance is pinned, navigate to instances page so the user can choose
            (App.MainWindowInstance as MainWindow)?.SelectNavTag("instances");
            return;
        }

        if (_launch.IsLaunching)
            return;

        if (!_launch.HasSignedInAccount)
        {
            await ShowNeedLoginAsync().ConfigureAwait(true);
            return;
        }

        if (_launch.Versions.FirstOrDefault(v =>
                string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) is null)
        {
            RefreshQuickLaunch();
            LaunchQuickCommand.NotifyCanExecuteChanged();
            (App.MainWindowInstance as MainWindow)?.SelectNavTag("instances");
            return;
        }

        if (App.MainWindowInstance is MainWindow mainWindow)
            mainWindow.NavigateToInstancesAndLaunch(id);
    }

    private static async Task ShowNeedLoginAsync()
    {
        var root = App.MainWindowInstance?.Content?.XamlRoot;
        if (root is null)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get(LocKeys.Nav_Account),
            Content = Loc.Get(LocKeys.Account_NeedLogin),
            CloseButtonText = Loc.Get(LocKeys.Action_Close)
        };
        await dialog.SafeShowAsync();
    }

    private void OnVersionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshQuickLaunch();
        LaunchQuickCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 5–8 早上好 · 9–11 上午好 · 12–13 中午好 · 14–17 下午好 · 18–4 晚上好
    /// </summary>
    internal static string ResolveGreetingKey(int hour) => hour switch
    {
        >= 5 and <= 8 => LocKeys.Home_GreetingEarlyMorning,
        >= 9 and <= 11 => LocKeys.Home_GreetingMorning,
        >= 12 and <= 13 => LocKeys.Home_GreetingNoon,
        >= 14 and <= 17 => LocKeys.Home_GreetingAfternoon,
        _ => LocKeys.Home_GreetingEvening
    };
}
