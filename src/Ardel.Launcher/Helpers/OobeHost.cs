using System.Diagnostics;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;
using Ardel.Launcher.Views.Oobe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Full-window first-run wizard. Choices persist only when the user finishes.
/// </summary>
internal static class OobeHost
{
    private static TaskCompletionSource<bool>? _completion;
    private static Grid? _overlay;
    private static OobeWizard? _wizard;
    private static Visibility _navVisibility;

    public static void Show(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_overlay is not null)
            return;

        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        AttachOverlay(window);
    }

    /// <summary>Re-open the setup wizard from Settings (does not reset saved preferences until finished).</summary>
    public static Task<bool> ReplayAsync(MainWindow window, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(services);
        Show(window);
        SeedFromSavedSettings(services);
        return RunAsync(window, services);
    }

    internal static void SyncOverlayTheme(string? themeCode)
    {
        if (_overlay is null)
            return;

        _overlay.RequestedTheme = AppThemeController.ResolveElementTheme(themeCode);
        // Snapshot after ApplyTheme (must be synchronous on UI thread) — never bind live
        // ThemeResource; Samoyed's canvas is fully transparent and would wash out the wizard.
        _overlay.Background = CreateOpaqueCanvasBrush(themeCode);
    }

    public static async Task<bool> RunAsync(MainWindow window, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(services);

        if (_overlay is null)
            Show(window);

        if (_wizard is null || _completion is null)
            return false;

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (_wizard is not null)
                _wizard.Completed -= handler;

            // Defer teardown: finishing inside the Next button click while the wizard is
            // still in the visual tree causes WinUI not-responding / process exit.
            var vm = _wizard!.ViewModel;
            var dq = window.DispatcherQueue;
            if (dq is null || !dq.TryEnqueue(() => OnCompleted(window, services, vm)))
                OnCompleted(window, services, vm);
        };
        _wizard.Completed += handler;

        return await _completion.Task.ConfigureAwait(true);
    }

    public static void SkipAndMarkComplete(IServiceProvider services)
    {
        try
        {
            var settingsService = services.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            settings.HasCompletedOobe = true;
            settings.AcceptedAboutLegalVersion = Localization.AboutLegalNotice.Version;
            settings.SchemaVersion = Math.Max(settings.SchemaVersion, 9);
            settingsService.Save(settings);
            Debug.WriteLine("[Oobe] Skipped and marked complete after failure.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] SkipAndMarkComplete failed: {ex.Message}");
        }
    }

    private static void AttachOverlay(MainWindow window)
    {
        try
        {
            OobeWindowChrome.Apply(window);
            TryApplyMicaBackdrop(window);
            HideShellChrome(window);

            _wizard = new OobeWizard
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _overlay = new Grid
            {
                Background = CreateOpaqueCanvasBrush(App.ActiveThemeCode),
                RequestedTheme = AppThemeController.ResolveElementTheme(App.ActiveThemeCode),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = true,
                Children = { _wizard }
            };
            Grid.SetRowSpan(_overlay, 2);
            Canvas.SetZIndex(_overlay, 10000);
            window.RootGrid.Children.Add(_overlay);
            App.ThemeChanged += OnAppThemeChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] Show failed: {ex}");
            _completion?.TrySetResult(false);
            RemoveOverlay(window);
        }
    }

    private static void OnAppThemeChanged() =>
        SyncOverlayTheme(App.ActiveThemeCode);

    private static void HideShellChrome(MainWindow window)
    {
        _navVisibility = window.NavView.Visibility;
        window.NavView.Visibility = Visibility.Collapsed;
    }

    private static void RestoreShellChrome(MainWindow window)
    {
        window.NavView.Visibility = _navVisibility;
    }

    private static void TryApplyMicaBackdrop(MainWindow window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] Mica backdrop unavailable: {ex.Message}");
        }
    }

    private static void OnCompleted(MainWindow window, IServiceProvider services, OobeViewModel vm)
    {
        try
        {
            var settingsService = services.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            settings.UiLanguage = vm.SelectedLanguageCode ?? string.Empty;
            settings.AppTheme = string.IsNullOrWhiteSpace(vm.SelectedThemeCode) ? "Default" : vm.SelectedThemeCode.Trim();
            settings.HasCompletedOobe = true;
            settings.AcceptedAboutLegalVersion = Localization.AboutLegalNotice.Version;
            settings.SchemaVersion = Math.Max(settings.SchemaVersion, 9);
            settingsService.Save(settings);

            // Language must apply before App resumes InitializeNavigation.
            // Do NOT call RelocalizeShell here: it resolves DownloadViewModel (cold,
            // expensive) and enqueues navigate-to-settings while the OOBE overlay is
            // still visible — feels like a freeze after 「开始使用」.
            // Skip typography here — ApplyLocalization (home nav) applies shell fonts once.
            App.ApplyUiLanguage(settings.UiLanguage, applyTypography: false);
            App.ApplyTheme(settings.AppTheme);

            // Keep in-memory VMs aligned with what we just wrote — otherwise Settings
            // chips / Launch Persist can still show or rewrite the pre-wizard values.
            try
            {
                services.GetRequiredService<LaunchViewModel>().SyncPersonalizationFromStore();
                services.GetService<SettingsViewModel>()?.ReloadAfterOobe();
            }
            catch (Exception syncEx)
            {
                Debug.WriteLine($"[Oobe] Post-complete VM sync failed: {syncEx.Message}");
            }

            RemoveOverlay(window);
            _completion?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] Complete failed: {ex.Message}");
            RemoveOverlay(window);
            _completion?.TrySetResult(false);
        }
    }

    private static void RemoveOverlay(MainWindow window)
    {
        if (_overlay is null)
            return;

        App.ThemeChanged -= OnAppThemeChanged;

        try
        {
            OobeWindowChrome.Restore(window);
            RestoreShellChrome(window);
            window.RootGrid.Children.Remove(_overlay);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] RemoveOverlay failed: {ex.Message}");
        }
        finally
        {
            _overlay = null;
            _wizard = null;
        }
    }

    private static void SeedFromSavedSettings(IServiceProvider services)
    {
        if (_wizard is null)
            return;

        try
        {
            var settings = services.GetRequiredService<SettingsService>().Load();
            if (!string.IsNullOrWhiteSpace(settings.UiLanguage))
                _wizard.ViewModel.SelectLanguage(settings.UiLanguage);

            var theme = string.IsNullOrWhiteSpace(settings.AppTheme) ? "Default" : settings.AppTheme.Trim();
            _wizard.ViewModel.SelectTheme(theme);
            App.ApplyTheme(theme);
            SyncOverlayTheme(theme);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Oobe] SeedFromSavedSettings failed: {ex.Message}");
        }
    }

    private static Brush CreateOpaqueCanvasBrush(string? themeCode = null)
    {
        themeCode = AppThemeController.NormalizeThemeCode(themeCode ?? App.ActiveThemeCode);

        // Samoyed (and any transparent canvas) must not punch through to Mica — OOBE text
        // uses ArdelInk / TextFillColor and becomes unreadable on a washed backdrop.
        if (string.Equals(themeCode, "Samoyed", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 245, 247, 250));

        if (Application.Current.Resources.TryGetValue("ArdelCanvasBrush", out var resource) &&
            resource is SolidColorBrush solid)
        {
            var c = solid.Color;
            if (c.A < 255)
            {
                return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, c.R, c.G, c.B));
            }

            return new SolidColorBrush(c);
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Black);
    }
}
