using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Globalization;
using Windows.System.UserProfile;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher;

public partial class App : Application
{
    private static MainWindow? _window;
    private static string _activeThemeCode = "Default";
    public static Window? MainWindowInstance => _window;
    public static string ActiveThemeCode => _activeThemeCode;

    /// <summary>Raised after shell theme / palette is applied (UI thread).</summary>
    public static event Action? ThemeChanged;

    /// <summary>True after the startup splash has closed and deferred home work may run.</summary>
    public static bool IsStartupComplete { get; private set; }

    /// <summary>Raised once on the UI thread when <see cref="IsStartupComplete"/> becomes true.</summary>
    public static event EventHandler? StartupCompleted;

    public static void MarkStartupComplete()
    {
        if (IsStartupComplete)
            return;

        IsStartupComplete = true;

        StartupCompleted?.Invoke(null, EventArgs.Empty);

        _ = Task.Run(async () =>
        {
            try
            {
                var accountVm = Services.GetService<ViewModels.AccountViewModel>();
                if (accountVm is not null)
                    await accountVm.EnsureMicrosoftSkinsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Background EnsureMicrosoftSkinsAsync failed: {ex.Message}");
            }
        });
    }

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Shared settings service created before DI so language load is not duplicated.</summary>
    private static SettingsService? _bootSettings;

    public App()
    {
        StartupClock.Mark("App ctor begin");
        // Language catalog must apply before any UI strings resolve.
        // Do NOT mutate ResourceDictionary FontFamily at runtime — unpackaged
        // WinUI stow-crashes (0xC000027B). Typography applies to the shell later.
        ApplyUiLanguage(LoadUiLanguagePreference(), applyTypography: false);
        InitializeComponent();
        StartupClock.Mark($"App InitializeComponent done (lang={Loc.ActiveLanguageTag})");
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Debug.WriteLine($"[App] UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Debug.WriteLine($"[App] AppDomain UnhandledException: {e.ExceptionObject}");
        };
    }

    public static void ApplyTheme(string? theme)
    {
        if (_window is null)
            return;

        _activeThemeCode = AppThemeController.NormalizeThemeCode(theme);
        StartupClock.Mark($"ApplyTheme begin ({_activeThemeCode})");
        var queue = _window.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

        // Already on the UI thread: apply now. Enqueueing would leave OOBE SyncOverlayTheme
        // (and launch-time Show) snapshotting stale ArdelCanvas/Ink brushes → washed-out /
        // inverted contrast until a later tick.
        if (queue is null || queue.HasThreadAccess)
        {
            AppThemeController.Apply(_window, _activeThemeCode);
            StartupClock.Mark("ApplyTheme done");
            ThemeChanged?.Invoke();
            return;
        }

        if (!queue.TryEnqueue(() =>
            {
                AppThemeController.Apply(_window, _activeThemeCode);
                StartupClock.Mark("ApplyTheme done");
                ThemeChanged?.Invoke();
            }))
        {
            AppThemeController.Apply(_window, _activeThemeCode);
            StartupClock.Mark("ApplyTheme done");
            ThemeChanged?.Invoke();
        }
    }

    public static void OnRootActualThemeChanged()
    {
        if (_window is null)
            return;

        if (string.Equals(_activeThemeCode, "Default", StringComparison.OrdinalIgnoreCase))
            ApplyTheme("Default");
        else
            ApplyCaptionButtonColors();
    }

    public static void ApplyCaptionButtonColors() =>
        AppThemeController.ApplyCaptionButtonColors(_window);

    public static void ApplyUiLanguage(string? preference, bool applyTypography = true)
    {
        try
        {
            var tag = ResolveLanguageTag(preference);
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = tag;
            }
            catch
            {
                // ignore
            }

            Loc.SetLanguage(tag);
            if (applyTypography)
                ApplyUiTypography(tag);

            LogLanguage($"ApplyUiLanguage pref='{preference}' -> '{tag}' nav='{Loc.Get(LocKeys.Nav_Instances)}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ApplyUiLanguage failed: {ex}");
            LogLanguage($"ApplyUiLanguage FAILED pref='{preference}': {ex.Message}");
            Loc.SetLanguage("en-US");
            if (applyTypography)
                ApplyUiTypography("en-US");
        }
    }

    public static void ApplyOobeUiLanguage(string? preference) =>
        // Skip typography during wizard language picks — FontFamily churn feels like a hitch.
        ApplyUiLanguage(preference, applyTypography: false);

    public static void ApplyUiTypography(string? languageTag) =>
        AppTypography.Apply(string.IsNullOrWhiteSpace(languageTag) ? Loc.ActiveLanguageTag : languageTag);

    public static string ResolveLanguageTag(string? preference)
    {
        if (!string.IsNullOrWhiteSpace(preference))
        {
            if (IsEnglishUkPreference(preference))
                return "en-UK";
            if (preference.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "en-US";
            if (IsTraditionalChinesePreference(preference))
                return "zh-Hant";
            if (preference.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";
            if (preference.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "ja-JP";
            if (preference.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                return "fr";
            if (preference.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                return "es";
            if (preference.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                return "ko-KR";
            if (preference.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                return "de";
            if (preference.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
                return "pt-BR";
            if (preference.StartsWith("it", StringComparison.OrdinalIgnoreCase))
                return "it";
            if (preference.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return "ru";
        }

        foreach (var lang in GlobalizationPreferences.Languages)
        {
            if (IsTraditionalChinesePreference(lang))
                return "zh-Hant";
            if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";
            if (IsEnglishUkPreference(lang))
                return "en-UK";
            if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "en-US";
            if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "ja-JP";
            if (lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                return "fr";
            if (lang.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                return "es";
            if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                return "ko-KR";
            if (lang.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                return "de";
            if (lang.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
                return "pt-BR";
            if (lang.StartsWith("it", StringComparison.OrdinalIgnoreCase))
                return "it";
            if (lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return "ru";
        }

        return "en-US";
    }

    private static bool IsTraditionalChinesePreference(string tag) =>
        tag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
        tag.Contains("-Hant", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("zh-CHT", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("zh-MO", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("zh-HK-", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("zh-MO-", StringComparison.OrdinalIgnoreCase) ||
        MatchesTraditionalRegion(tag);

    private static bool MatchesTraditionalRegion(string tag)
    {
        var parts = tag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var part in parts)
        {
            if (part.Equals("Hant", StringComparison.OrdinalIgnoreCase))
                return true;
            if (part.Equals("TW", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("HK", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("MO", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsEnglishUkPreference(string tag) =>
        tag.Equals("en-UK", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("en-GB", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("en-GB-", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("en-AU", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("en-NZ", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("en-IE", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("en-ZA", StringComparison.OrdinalIgnoreCase);

    public static void RelocalizeShell(string? preference)
    {
        ApplyUiLanguage(preference);

        Services.GetService<HomeViewModel>()?.Relocalize();
        if (Services.GetService<DownloadViewModel>() is { } downloads)
            downloads.Relocalize();
        Services.GetRequiredService<LaunchViewModel>().Relocalize();
        Services.GetService<AccountViewModel>()?.Relocalize();
        Services.GetRequiredService<SettingsViewModel>().Relocalize();
        Services.GetRequiredService<AboutViewModel>().Relocalize();
        _ = Services.GetRequiredService<SkinLibraryStore>().EnsureReadyAsync();

        if (_window is null)
        {
            LogLanguage("RelocalizeShell: no window");
            return;
        }

        // Never tear down the active page synchronously from a control callback (e.g. ListView ItemClick).
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (_window is null)
                return;

            _window.ApplyLocalization(navigateToTag: "settings");
            LogLanguage($"RelocalizeShell done tag={Loc.ActiveLanguageTag}");
        });
    }

    private static void LogLanguage(string message)
    {
        // Never block startup on disk I/O — Debug + fire-and-forget append.
        Debug.WriteLine($"[Lang] {message}");
        _ = Task.Run(() =>
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ardel");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "language.log"),
                    $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
            catch
            {
                // ignore
            }
        });
    }

    private static string LoadUiLanguagePreference()
    {
        try
        {
            _bootSettings ??= new SettingsService();
            return _bootSettings.Load().UiLanguage ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupClock.Mark("OnLaunched begin");

        var savedTheme = "Default";
        StartupSplashOptions splashOptions = new();
        var needOobe = false;
        try
        {
            var bootSettings = _bootSettings ??= new SettingsService();
            bootSettings.InvalidateCache();
            var settings = bootSettings.Load();
            savedTheme = settings.AppTheme ?? "Default";
            splashOptions = StartupSplashOptions.FromSettings(settings);
            needOobe = !settings.HasCompletedOobe
                || settings.AcceptedAboutLegalVersion < Localization.AboutLegalNotice.Version;
        }
        catch
        {
            // keep defaults
        }

        // One window only — splash is an in-content overlay (second Window can
        // tear down the process under WASDK when it hides/closes).
        _window = new MainWindow();
        StartupClock.Mark("MainWindow created");

        Services = ConfigureServices(_window);
        StartupClock.Mark("DI configured");

        try
        {
            // Must go through ApplyTheme so _activeThemeCode matches — otherwise
            // ActualThemeChanged re-applies "Default" (often dark) and wipes the palette.
            ApplyTheme(savedTheme);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ApplyTheme on launch failed: {ex.Message}");
        }

        StartupClock.Mark("Navigate begin");
        if (!needOobe)
            StartupClock.Mark("Navigate deferred until after splash");
        StartupClock.Mark("Navigate done");

        if (needOobe)
            OobeHost.Show(_window);

        StartupClock.Mark("Activate begin");
        _window.Activate();
        
        // Ensure the AppWindow is explicitly shown, bypassing any inherited hidden states
        try { _window.AppWindow.Show(); } catch { }
        TryForceWindowToFront(_window);

        StartupClock.Mark("Window Activated");

        if (!needOobe && splashOptions.Enabled)
        {
            StartupSplash.Show(_window, savedTheme, splashOptions);
            StartupClock.Mark("Splash overlay shown");
        }

        var dq = DispatcherQueue.GetForCurrentThread();
        dq.TryEnqueue(async () =>
        {
            if (needOobe)
            {
                StartupClock.Mark("OOBE begin");
                StartupClock.Flush();
                var oobeOk = false;
                try
                {
                    oobeOk = await OobeHost.RunAsync(_window, Services).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] OOBE failed: {ex}");
                    StartupClock.Mark($"OOBE failed: {ex.Message}");
                }

                if (!oobeOk)
                    Debug.WriteLine("[App] OOBE did not complete; HasCompletedOobe left unchanged for retry on next launch.");

                StartupClock.Mark("OOBE done");
                StartupClock.Flush();

                // Skip startup splash after OOBE — the wizard already took the user's time.
                _window.InitializeNavigation();
                StartupClock.Mark("Navigate done after OOBE");

                dq.TryEnqueue(MarkStartupComplete);
            }
            else
            {
                StartupClock.Mark("Navigate begin (deferred)");
                _window.InitializeNavigation();
                StartupClock.Mark("Navigate done (deferred)");

                if (splashOptions.Enabled)
                    await StartupSplash.CloseWhenReadyAsync(_window, splashOptions.DurationMs).ConfigureAwait(false);

                StartupClock.Mark("Splash closed");
                dq.TryEnqueue(MarkStartupComplete);
            }

            StartupClock.Flush();

            var launchVm = Services.GetRequiredService<LaunchViewModel>();
            launchVm.GameExited += (_, _) => _window?.Activate();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    _ = GamePaths.GetMinecraftRoot();
                    GamePaths.PurgeIncompleteAndTrash();
                    GamePaths.MigrateLaunchReadyMarkers();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Startup maintenance failed: {ex.Message}");
                }
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Services.GetRequiredService<SkinLibraryStore>()
                        .EnsureReadyAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Skin seed failed: {ex.Message}");
                }
            });
        });
    }

    private static ServiceProvider ConfigureServices(Window window)
    {
        var services = new ServiceCollection();

        services.AddSingleton(window);
        services.AddSingleton(DispatcherQueue.GetForCurrentThread());
        // Reuse boot SettingsService so language Load() is not paid twice.
        services.AddSingleton(_bootSettings ??= new SettingsService());
        services.AddSingleton<InstanceSettingsStore>();
        services.AddSingleton<InstanceStatsStore>();
        services.AddSingleton<LocalVersionStore>();
        services.AddSingleton(sp => new Lazy<IMinecraftLaunchService>(
            () => MinecraftLaunchServiceFactory.Create(
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<InstanceSettingsStore>())));
        services.AddSingleton<WeatherService>();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<SkinLibraryStore>();
        services.AddSingleton<Services.Auth.MicrosoftAuthService>();
        services.AddSingleton<LaunchViewModel>();
        services.AddSingleton<DownloadViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<InstancesViewModel>();
        services.AddTransient<InstanceSettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        return services.BuildServiceProvider();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"[App] Unhandled: {e.Exception}");
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ardel");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"{DateTimeOffset.Now:o} {e.Exception}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    private static void TryForceWindowToFront(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
                return;

            ShowWindow(hwnd, 5); // SW_SHOW
            var fgHwnd = GetForegroundWindow();
            var myThreadId = GetWindowThreadProcessId(hwnd, out _);
            var fgThreadId = fgHwnd != IntPtr.Zero
                ? GetWindowThreadProcessId(fgHwnd, out _)
                : myThreadId;

            var attached = fgThreadId != myThreadId
                && AttachThreadInput(fgThreadId, myThreadId, true);
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(fgThreadId, myThreadId, false);
            }
        }
        catch
        {
            // ignore
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
