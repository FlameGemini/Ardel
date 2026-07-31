using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Globalization;
using Windows.System.UserProfile;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher;

public partial class App : Application
{
    private static MainWindow? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        StartupClock.Mark("App ctor begin");
        ApplyUiLanguage(LoadUiLanguagePreference());
        InitializeComponent();
        StartupClock.Mark("App InitializeComponent done");
        UnhandledException += OnUnhandledException;
    }

    public static void ApplyUiLanguage(string? preference)
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
            LogLanguage($"ApplyUiLanguage pref='{preference}' -> '{tag}' nav='{Loc.Get(LocKeys.Nav_Play)}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ApplyUiLanguage failed: {ex.Message}");
            Loc.SetLanguage("en-US");
        }
    }

    public static string ResolveLanguageTag(string? preference)
    {
        if (string.Equals(preference, "en-US", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "en", StringComparison.OrdinalIgnoreCase))
            return "en-US";

        if (string.Equals(preference, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        if (string.Equals(preference, "ja-JP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "ja", StringComparison.OrdinalIgnoreCase))
            return "ja-JP";

        foreach (var lang in GlobalizationPreferences.Languages)
        {
            if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";
            if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "ja-JP";
        }

        return "en-US";
    }

    /// <summary>
    /// Switch language in-process and refresh the existing shell (no window recreate —
    /// closing the old WinUI window often tears down the process).
    /// </summary>
    public static void RelocalizeShell(string? preference)
    {
        ApplyUiLanguage(preference);

        Services.GetRequiredService<DownloadViewModel>().Relocalize();

        if (_window is null)
        {
            LogLanguage("RelocalizeShell: no window");
            return;
        }

        _window.ApplyLocalization();
        LogLanguage($"RelocalizeShell done tag={Loc.ActiveLanguageTag}");
    }

    private static void LogLanguage(string message)
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
    }

    private static string LoadUiLanguagePreference()
    {
        try
        {
            return new SettingsService().Load().UiLanguage ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupClock.Mark("OnLaunched begin");

        _window = new MainWindow();
        StartupClock.Mark("MainWindow created");

        Services = ConfigureServices(_window);
        StartupClock.Mark("DI configured");

        _window.Activate();
        StartupClock.Mark("Window Activated");

        var dq = DispatcherQueue.GetForCurrentThread();
        dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            StartupClock.Mark("Navigate begin");
            _window!.InitializeNavigation();
            StartupClock.Mark("Navigate done");
            StartupClock.Flush();

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
        });
    }

    private static ServiceProvider ConfigureServices(Window window)
    {
        var services = new ServiceCollection();

        services.AddSingleton(window);
        services.AddSingleton(DispatcherQueue.GetForCurrentThread());
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalVersionStore>();
        services.AddSingleton(sp => new Lazy<IMinecraftLaunchService>(
            () => MinecraftLaunchServiceFactory.Create(sp.GetRequiredService<SettingsService>())));
        services.AddSingleton<LaunchViewModel>();
        services.AddSingleton<DownloadViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<InstancesViewModel>();

        return services.BuildServiceProvider();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"[App] Unhandled: {e.Exception}");
        e.Handled = true;
    }
}
