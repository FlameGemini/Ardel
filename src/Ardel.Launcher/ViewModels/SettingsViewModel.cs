using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LaunchViewModel _launchViewModel;
    private readonly SettingsService _settingsService;
    private readonly Window _window;
    private bool _javaScanned;
    private bool _javaScanning;
    private bool _suppressLanguagePersist;

    public SettingsViewModel(
        LaunchViewModel launchViewModel,
        SettingsService settingsService,
        Window window)
    {
        _launchViewModel = launchViewModel;
        _settingsService = settingsService;
        _window = window;

        LanguageOptions =
        [
            new LanguageOption(string.Empty, Loc.Get(LocKeys.Settings_LanguageSystem)),
            new LanguageOption("en-US", Loc.Get(LocKeys.Settings_LanguageEnglish)),
            new LanguageOption("zh-CN", Loc.Get(LocKeys.Settings_LanguageChinese)),
            new LanguageOption("ja-JP", Loc.Get(LocKeys.Settings_LanguageJapanese))
        ];

        SyncFromLaunch();
    }

    public ObservableCollection<JavaInstallation> JavaInstallations => _launchViewModel.JavaInstallations;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxRamMb = 4096;
    [ObservableProperty] private bool _useBmclApi;
    [ObservableProperty] private string _gameDirectory = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _javaVersionHint = string.Empty;
    /// <summary>"" = system, "en-US", "zh-CN". Bound via ComboBox.SelectedValue.</summary>
    [ObservableProperty] private string _uiLanguageCode = string.Empty;
    [ObservableProperty] private bool _isJavaBusy;
    [ObservableProperty] private bool _canEditJava = true;

    partial void OnIsJavaBusyChanged(bool value) => CanEditJava = !value;

    partial void OnJavaPathChanged(string? value)
    {
        _launchViewModel.JavaPath = value;
        _ = UpdateJavaHintAsync();
    }

    partial void OnMaxRamMbChanged(int value) => _launchViewModel.MaxRamMb = value;

    partial void OnUseBmclApiChanged(bool value)
    {
        _launchViewModel.UseBmclApi = value;
        StatusText = value
            ? Loc.Get(LocKeys.Settings_SourceBmcl)
            : Loc.Get(LocKeys.Settings_SourceOfficial);
    }

    partial void OnUiLanguageCodeChanged(string value)
    {
        if (_suppressLanguagePersist)
            return;

        // Persist immediately; user still clicks Apply to refresh the shell.
        try
        {
            var settings = _launchViewModel.SnapshotSettings();
            settings.UiLanguage = value ?? string.Empty;
            _settingsService.Save(settings);
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
            return;

        await RescanJavaAsync().ConfigureAwait(true);
        _javaScanned = true;
    }

    [RelayCommand]
    private async Task RescanJavaAsync()
    {
        if (_javaScanning)
            return;

        _javaScanning = true;
        IsJavaBusy = true;
        StatusText = Loc.Get(LocKeys.Settings_ScanningJava);
        try
        {
            var javas = await Task.Run(JavaLocator.FindInstallations).ConfigureAwait(true);

            _launchViewModel.JavaInstallations.Clear();
            foreach (var java in javas)
                _launchViewModel.JavaInstallations.Add(java);

            JavaPath = _launchViewModel.JavaPath;
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
            _javaScanning = false;
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
    private void Save()
    {
        try
        {
            var settings = _launchViewModel.SnapshotSettings();
            settings.GameDirectory = GamePaths.GetMinecraftRoot();
            settings.ForceVersionIsolation = true;
            settings.UiLanguage = UiLanguageCode ?? string.Empty;
            _settingsService.Save(settings);
            GameDirectory = settings.GameDirectory;
            StatusText = Loc.Get(LocKeys.Settings_Saved);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void ApplyLanguage()
    {
        try
        {
            var code = UiLanguageCode ?? string.Empty;
            var settings = _launchViewModel.SnapshotSettings();
            settings.UiLanguage = code;
            _settingsService.Save(settings);

            App.RelocalizeShell(code);

            StatusText = Loc.Format(
                LocKeys.Settings_LanguageApplied,
                Loc.ActiveLanguageTag,
                Loc.Get(LocKeys.Nav_Play));
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format(LocKeys.Settings_SaveFailed, ex.Message);
            Debug.WriteLine(ex);
        }
    }

    public void SyncFromLaunch()
    {
        JavaPath = _launchViewModel.JavaPath;
        MaxRamMb = _launchViewModel.MaxRamMb;
        UseBmclApi = _launchViewModel.UseBmclApi;
        GameDirectory = GamePaths.GetMinecraftRoot();

        var saved = _settingsService.Load().UiLanguage ?? string.Empty;
        _suppressLanguagePersist = true;
        UiLanguageCode = saved;
        _suppressLanguagePersist = false;

        _ = UpdateJavaHintAsync();
    }

    private async Task UpdateJavaHintAsync()
    {
        var path = JavaPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            JavaVersionHint = Loc.Get(LocKeys.Settings_JavaAuto);
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
    }
}

public sealed record LanguageOption(string Code, string Label)
{
    public override string ToString() => Label;
}
