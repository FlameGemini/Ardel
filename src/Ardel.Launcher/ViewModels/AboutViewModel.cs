using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services.Update;

namespace Ardel.Launcher.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private static IReadOnlyList<AboutLegalSection>? _cachedLegalSections;
    private static string? _cachedLegalText;
    private static string? _cachedLegalHeading;

    private readonly UpdateService _updateService;
    private UpdateInfo? _currentUpdateInfo;
    private string? _downloadedUpdatePath;
    private bool _initialized;

    public AboutViewModel(UpdateService? updateService = null)
    {
        _updateService = updateService ?? new UpdateService();
        VersionNumber = AboutMetadata.ResolveVersionNumber();
        App.ThemeChanged += OnAppThemeChanged;
    }

    /// <summary>Loads localized credits and legal sections on first About visit.</summary>
    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        RefreshLocalized();
        RebuildCredits();
        _ = SyncBrandLogoAsync();
        _ = CheckUpdateAsync(silent: true);
    }

    public string VersionNumber { get; }

    [ObservableProperty] private string _versionLabel = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _copyrightLabel = string.Empty;
    [ObservableProperty] private BitmapImage? _brandLogoImage;
    [ObservableProperty] private IReadOnlyList<AboutCreditGroup> _creditGroups = Array.Empty<AboutCreditGroup>();
    [ObservableProperty] private IReadOnlyList<AboutLegalSection> _legalSections = Array.Empty<AboutLegalSection>();
    [ObservableProperty] private string _legalUpdatedLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowCheckUpdateButton))]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCheckUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowApplyUpdateButton))]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private bool _hasUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCheckUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowApplyUpdateButton))]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    private bool _isUpdateReadyToRestart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateStatusText))]
    private string _updateStatusText = string.Empty;

    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private string _latestVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private string _releaseNotesUrl = string.Empty;

    [ObservableProperty] private string _releaseNotes = string.Empty;

    public bool CanCheckUpdate => !IsCheckingUpdate && !IsDownloadingUpdate;
    public bool CanApplyUpdate => !IsDownloadingUpdate;
    public bool ShowCheckUpdateButton => !HasUpdate && !IsUpdateReadyToRestart;
    public bool ShowApplyUpdateButton => HasUpdate || IsUpdateReadyToRestart;
    public bool HasUpdateStatusText => !string.IsNullOrWhiteSpace(UpdateStatusText);
    public bool HasReleaseNotes => HasUpdate && !string.IsNullOrWhiteSpace(ReleaseNotesUrl);
    public string UpdateActionLabel => IsUpdateReadyToRestart
        ? Loc.Get(LocKeys.Settings_UpdateRestart)
        : Loc.Get(LocKeys.Action_Download);

    public void Relocalize()
    {
        if (!_initialized)
            return;

        RefreshLocalized();
        RebuildCredits();
        _ = SyncBrandLogoAsync();

        if (IsUpdateReadyToRestart)
        {
            UpdateStatusText = Loc.Get(LocKeys.Settings_UpdateRestart);
        }
        else if (HasUpdate && !string.IsNullOrEmpty(LatestVersion))
        {
            UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateAvailable, LatestVersion);
        }
        else if (!IsCheckingUpdate && !IsDownloadingUpdate && !string.IsNullOrEmpty(UpdateStatusText))
        {
            UpdateStatusText = Loc.Get(LocKeys.Settings_UpdateUpToDate);
        }
    }

    [RelayCommand]
    public async Task CheckUpdateManualAsync() => await CheckUpdateAsync(silent: false);

    public async Task CheckUpdateAsync(bool silent = false)
    {
        if (IsCheckingUpdate || IsDownloadingUpdate)
            return;

        IsCheckingUpdate = true;
        if (!silent)
        {
            UpdateStatusText = Loc.Get(LocKeys.Settings_UpdateChecking);
        }

        try
        {
            var info = await _updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            _currentUpdateInfo = info;
            if (info.HasUpdate)
            {
                HasUpdate = true;
                LatestVersion = info.LatestVersion;
                ReleaseNotesUrl = info.ReleaseUrl;
                ReleaseNotes = info.ReleaseNotes;
                UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateAvailable, info.LatestVersion);
            }
            else
            {
                HasUpdate = false;
                if (!silent)
                {
                    UpdateStatusText = Loc.Get(LocKeys.Settings_UpdateUpToDate);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutViewModel] Check update failed: {ex.Message}");
            if (!silent)
            {
                UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateFailed, ex.Message);
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (IsUpdateReadyToRestart && !string.IsNullOrEmpty(_downloadedUpdatePath))
        {
            _updateService.ApplyUpdateAndRestart(_downloadedUpdatePath);
            return;
        }

        if (_currentUpdateInfo == null || !_currentUpdateInfo.HasUpdate)
            return;

        if (IsDownloadingUpdate)
            return;

        IsDownloadingUpdate = true;
        UpdateProgress = 0;
        UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateDownloading, 0);

        try
        {
            var progressHandler = new Progress<double>(p =>
            {
                UpdateProgress = p;
                UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateDownloading, p);
            });

            var path = await _updateService.DownloadUpdateAsync(_currentUpdateInfo, progressHandler).ConfigureAwait(true);
            _downloadedUpdatePath = path;
            IsUpdateReadyToRestart = true;
            UpdateStatusText = Loc.Get(LocKeys.Settings_UpdateRestart);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutViewModel] Download update failed: {ex.Message}");
            UpdateStatusText = Loc.Format(LocKeys.Settings_UpdateFailed, ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    public async Task OpenReleaseNotesAsync()
    {
        var targetUrl = !string.IsNullOrEmpty(ReleaseNotesUrl)
            ? ReleaseNotesUrl
            : "https://github.com/FlameGemini/Ardel/releases";

        await Windows.System.Launcher.LaunchUriAsync(new Uri(targetUrl));
    }

    private void OnAppThemeChanged() => _ = SyncBrandLogoAsync();

    private void RefreshLocalized()
    {
        VersionLabel = Loc.Format(LocKeys.About_Version, VersionNumber);
        Subtitle = Loc.Get(LocKeys.About_Subtitle);
        CopyrightLabel = AboutMetadata.ResolveCopyright();
        LegalUpdatedLabel = Loc.Format(
            LocKeys.About_LegalUpdated,
            AboutLegalNotice.Version,
            AboutLegalNotice.EffectiveDate);
        var legalNotice = Loc.Get(LocKeys.About_Disclaimer);
        var legalHeading = Loc.Get(LocKeys.About_LegalHeading);
        LegalSections = GetOrParseLegalSections(legalNotice, legalHeading);
    }

    private static IReadOnlyList<AboutLegalSection> GetOrParseLegalSections(string text, string legalHeading)
    {
        if (_cachedLegalSections is not null &&
            string.Equals(_cachedLegalText, text, StringComparison.Ordinal) &&
            string.Equals(_cachedLegalHeading, legalHeading, StringComparison.Ordinal))
        {
            return _cachedLegalSections;
        }

        _cachedLegalText = text;
        _cachedLegalHeading = legalHeading;
        _cachedLegalSections = ParseLegalSections(text, legalHeading);
        return _cachedLegalSections;
    }

    private static IReadOnlyList<AboutLegalSection> ParseLegalSections(string text, string legalHeading)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<AboutLegalSection>();

        var sections = new List<AboutLegalSection>();
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseLegalBlock(block, legalHeading) is { } section)
                sections.Add(section);
        }

        if (sections.Count > 0 &&
            !string.Equals(sections[0].Title, legalHeading, StringComparison.Ordinal))
        {
            var first = sections[0];
            var body = string.IsNullOrWhiteSpace(first.Title)
                ? first.Body
                : $"{first.Title}\n{first.Body}";
            sections[0] = new AboutLegalSection { Title = legalHeading, Body = body };
        }

        return sections;
    }

    private static AboutLegalSection? TryParseLegalBlock(string block, string legalHeading)
    {
        var newline = block.IndexOf('\n');
        if (newline > 0)
        {
            var title = block[..newline].Trim();
            var body = block[(newline + 1)..].Trim();
            if (AboutLegalNotice.IsVersionPreambleLine(title))
            {
                return string.IsNullOrEmpty(body)
                    ? null
                    : new AboutLegalSection { Title = legalHeading, Body = body };
            }

            return new AboutLegalSection { Title = title, Body = body };
        }

        var line = block.Trim();
        if (AboutLegalNotice.IsVersionPreambleLine(line))
            return null;

        return new AboutLegalSection { Title = line, Body = string.Empty };
    }

    [RelayCommand]
    private void CopyVersion()
    {
        var package = new DataPackage();
        package.SetText(VersionNumber);
        Clipboard.SetContent(package);
    }

    [RelayCommand]
    private async Task OpenSourceAsync()
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri(AboutMetadata.SourceRepositoryUrl));
    }

    [RelayCommand]
    private async Task OpenLicenseAsync()
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri(AboutMetadata.LicenseUrl));
    }

    [RelayCommand]
    private async Task OpenCreditAsync(AboutCreditItem? item)
    {
        if (item?.Link is not { } uri)
            return;

        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand]
    private async Task OpenLegalFeedbackAsync()
    {
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/FlameGemini/Ardel/issues"));
    }

    [RelayCommand]
    private async Task OpenNoticeFileAsync()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var noticePath = Path.Combine(baseDir, "NOTICE.txt");
            if (!File.Exists(noticePath))
            {
                var candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "NOTICE.txt"));
                if (File.Exists(candidate))
                    noticePath = candidate;
            }

            if (File.Exists(noticePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = noticePath,
                    UseShellExecute = true
                });
                return;
            }

            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/FlameGemini/Ardel/blob/main/NOTICE.txt"));
        }
        catch
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(
                    new Uri("https://github.com/FlameGemini/Ardel/blob/main/NOTICE.txt"));
            }
            catch
            {
                // Ignore failure
            }
        }
    }

    private async Task SyncBrandLogoAsync()
    {
        try
        {
            var lightShell = !(App.MainWindowInstance is MainWindow mw
                ? AppThemeController.ResolveSystemIsDark(mw)
                : Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var scale = App.MainWindowInstance?.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            var px = (int)Math.Clamp(Math.Ceiling(96 * scale * 2), 128, 512);
            BrandLogoImage = await ArdelLogoRenderer.CreateAsync(px, lightShell).ConfigureAwait(true);
        }
        catch
        {
            var lightShell = Application.Current.RequestedTheme == ApplicationTheme.Light;
            BrandLogoImage = new BitmapImage(new Uri(lightShell
                ? "ms-appx:///Assets/ardel-logo-ink.png"
                : "ms-appx:///Assets/ardel-logo.png"));
        }
    }

    private void RebuildCredits()
    {
        CreditGroups =
        [
            new AboutCreditGroup
            {
                HeadingLocKey = LocKeys.About_LibrariesHeading,
                Items =
                [
                    Credit("CmlLib.Core", LocKeys.About_Credit_CmlLib, "MIT",
                        "https://github.com/CmlLib/CmlLib.Core"),
                    Credit("CmlLib.Core.Installer.Forge", LocKeys.About_Credit_CmlLibForge, "MIT",
                        "https://github.com/CmlLib/CmlLib.Core.Installer.Forge"),
                    Credit("CmlLib.Core.Installer.NeoForge", LocKeys.About_Credit_CmlLibForge, "MIT",
                        "https://github.com/Gml-Launcher/CmlLib.Core.Installer.NeoForge"),
                    Credit("Optifine.Installer", LocKeys.About_Credit_OptifineInstaller, null,
                        "https://github.com/mzggr0914/Optifine.Installer"),
                    Credit("CommunityToolkit.Mvvm", LocKeys.About_Credit_CommunityToolkit, "MIT",
                        "https://github.com/CommunityToolkit/dotnet"),
                    Credit("Microsoft.Extensions.DependencyInjection", LocKeys.About_Credit_MsDi, "MIT",
                        "https://github.com/dotnet/runtime"),
                    Credit("Microsoft.WindowsAppSDK / WinUI 3", LocKeys.About_Credit_Wasdk, "MIT",
                        "https://github.com/microsoft/WindowsAppSDK"),
                    Credit(".NET 8", LocKeys.About_Credit_DotNet, "MIT",
                        "https://github.com/dotnet/runtime"),
                    Credit("authlib-injector", LocKeys.About_Credit_AuthlibInjector, null,
                        "https://github.com/yushijinhun/authlib-injector"),
                    Credit("MinecraftSkinRender", LocKeys.About_Credit_MinecraftSkinRender, "MIT",
                        "https://github.com/Coloryr/MinecraftSkinRender"),
                    Credit("SkiaSharp", LocKeys.About_Credit_SkiaSharp, "MIT",
                        "https://github.com/mono/SkiaSharp"),
                    Credit("XboxAuthNet", LocKeys.About_Credit_XboxAuthNet, "MIT",
                        "https://github.com/CmlLib/XboxAuthNet"),
                ]
            },
            new AboutCreditGroup
            {
                HeadingLocKey = LocKeys.About_ServicesHeading,
                Items =
                [
                    Credit("BMCLAPI", LocKeys.About_Credit_BmclApi, null,
                        "https://bmclapidoc.bangbang93.com"),
                    Credit("Modrinth API", LocKeys.About_Credit_Modrinth, null,
                        "https://modrinth.com"),
                    Credit("CurseForge API (via curse.tools)", LocKeys.About_Credit_CurseForge, null,
                        "https://api.curse.tools"),
                    Credit("Adoptium API", LocKeys.About_Credit_Adoptium, null,
                        "https://adoptium.net"),
                    Credit("Open-Meteo", LocKeys.About_Credit_OpenMeteo, null,
                        "https://open-meteo.com"),
                    Credit("OpenStreetMap Nominatim", LocKeys.About_Credit_Nominatim, null,
                        "https://nominatim.org"),
                ]
            },
            new AboutCreditGroup
            {
                HeadingLocKey = LocKeys.About_FontsHeading,
                Items =
                [
                    Credit("JetBrains Mono", LocKeys.About_Credit_JetBrainsMono, "OFL-1.1",
                        "https://www.jetbrains.com/lp/mono/"),
                    Credit("Consolas", LocKeys.About_Credit_Consolas, "Windows", null),
                    Credit("Segoe UI Variable", LocKeys.About_Credit_SegoeUiVariable, "Windows", null),
                    Credit("Microsoft YaHei UI", LocKeys.About_Credit_YaHeiUi, "Windows", null),
                    Credit("Microsoft JhengHei UI", LocKeys.About_Credit_JhengHeiUi, "Windows", null),
                    Credit("Yu Gothic UI", LocKeys.About_Credit_YuGothicUi, "Windows", null),
                    Credit("Meiryo UI", LocKeys.About_Credit_MeiryoUi, "Windows", null),
                    Credit("Malgun Gothic", LocKeys.About_Credit_MalgunGothic, "Windows", null),
                    Credit("Segoe UI", LocKeys.About_Credit_SegoeUi, "Windows", null),
                    Credit("Segoe Fluent Icons", LocKeys.About_Credit_SegoeFluentIcons, "Windows", null),
                ]
            },
        ];
    }

    private static AboutCreditItem Credit(string name, string summaryKey, string? license, string? url) =>
        new()
        {
            Name = name,
            SummaryLocKey = summaryKey,
            License = license,
            Url = url
        };
}
