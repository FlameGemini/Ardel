using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Views;

/// <summary>
/// Install options: exactly one of Forge / Fabric / NeoForge / OptiFine / none.
/// </summary>
public sealed partial class InstallVersionDialog : UserControl
{
    private readonly string _minecraftVersionId;
    private readonly string _versionsRoot;
    private readonly IMinecraftLaunchService _launchService;
    private readonly LauncherSettings _settings;
    private readonly HashSet<string> _existingNames;

    private int _loaderLoadGeneration;
    private bool _loaderVersionsReady = true;
    private string? _lastSuggestedName;
    private bool _suppressNameChanged;

    public InstallVersionDialog(
        string minecraftVersionId,
        string versionsRoot,
        IMinecraftLaunchService launchService,
        LauncherSettings settings)
    {
        _minecraftVersionId = minecraftVersionId;
        _versionsRoot = versionsRoot;
        _launchService = launchService;
        _settings = settings;
        _existingNames = LoadExistingNames(versionsRoot);

        InitializeComponent();
        VersionNameBox.Text = minecraftVersionId;
        _lastSuggestedName = minecraftVersionId;
        VersionNameBox.TextChanged += (_, _) =>
        {
            if (!_suppressNameChanged)
                RefreshValidation();
        };
        LoaderVersionBox.SelectionChanged += (_, _) =>
        {
            if (LoaderVersionBox.SelectedItem is ModLoaderVersionOption opt)
                SuggestVersionNameForLoader(SelectedLoader(), opt.Id);
            RefreshValidation();
        };

        LoaderNone.Checked += OnLoaderRadioChecked;
        LoaderFabric.Checked += OnLoaderRadioChecked;
        LoaderForge.Checked += OnLoaderRadioChecked;
        LoaderNeoForge.Checked += OnLoaderRadioChecked;
        LoaderOptiFine.Checked += OnLoaderRadioChecked;

        RefreshFabricApiVisibility();
        RefreshValidation();
    }

    public bool IsValid { get; private set; }

    public event EventHandler? ValidityChanged;

    public InstallRequest? BuildRequest()
    {
        if (!IsValid)
            return null;

        var loader = SelectedLoader();
        string? loaderVersion = null;
        if (loader != ModLoaderKind.None && LoaderVersionBox.SelectedItem is ModLoaderVersionOption opt)
            loaderVersion = opt.Id;

        return new InstallRequest
        {
            MinecraftVersionId = _minecraftVersionId,
            CustomVersionName = VersionNameBox.Text,
            Loader = loader,
            LoaderVersion = loaderVersion,
            InstallFabricApi = loader == ModLoaderKind.Fabric && FabricApiCheck.IsChecked == true
        };
    }

    public static async Task<InstallRequest?> ShowAsync(
        XamlRoot xamlRoot,
        GameVersionItem version,
        string versionsRoot,
        IMinecraftLaunchService launchService,
        LauncherSettings settings)
    {
        await Task.Yield();

        var content = new InstallVersionDialog(version.Id, versionsRoot, launchService, settings);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Format(LocKeys.Install_Title, version.Id),
            PrimaryButtonText = Loc.Get(LocKeys.Action_Download),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        void OnValidity(object? _, EventArgs __) =>
            dialog.IsPrimaryButtonEnabled = content.IsValid;

        content.ValidityChanged += OnValidity;
        dialog.IsPrimaryButtonEnabled = content.IsValid;

        try
        {
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? content.BuildRequest() : null;
        }
        finally
        {
            content.ValidityChanged -= OnValidity;
        }
    }

    private void OnLoaderRadioChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: not true })
            return;

        RefreshFabricApiVisibility();
        SuggestVersionNameForLoader(SelectedLoader(), loaderVersion: null);
        _ = ReloadLoaderVersionsAsync();
    }

    private void RefreshFabricApiVisibility()
    {
        var show = SelectedLoader() == ModLoaderKind.Fabric;
        FabricApiCheck.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FabricApiHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SuggestVersionNameForLoader(ModLoaderKind loader, string? loaderVersion)
    {
        var suggested = VersionKindDetector.SuggestName(_minecraftVersionId, loader, loaderVersion);
        var current = VersionNameBox.Text ?? string.Empty;

        // Only auto-replace when the user hasn't customized the name.
        var isDefaultOrPreviousSuggestion =
            string.IsNullOrWhiteSpace(current) ||
            string.Equals(current, _minecraftVersionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current, _lastSuggestedName, StringComparison.OrdinalIgnoreCase);

        if (!isDefaultOrPreviousSuggestion)
            return;

        _suppressNameChanged = true;
        VersionNameBox.Text = suggested;
        _lastSuggestedName = suggested;
        _suppressNameChanged = false;
    }

    private async Task ReloadLoaderVersionsAsync()
    {
        var loader = SelectedLoader();
        var generation = ++_loaderLoadGeneration;

        if (loader == ModLoaderKind.None)
        {
            LoaderVersionPanel.Visibility = Visibility.Collapsed;
            LoaderVersionBox.ItemsSource = null;
            _loaderVersionsReady = true;
            LoaderStatusText.Text = string.Empty;
            SuggestVersionNameForLoader(ModLoaderKind.None, null);
            RefreshValidation();
            return;
        }

        LoaderVersionPanel.Visibility = Visibility.Visible;
        _loaderVersionsReady = false;
        LoaderVersionBox.ItemsSource = null;
        LoaderVersionBox.IsEnabled = false;
        LoaderStatusText.Text = loader == ModLoaderKind.OptiFine
            ? Loc.Get(LocKeys.Install_LoadingOptiFine)
            : Loc.Get(LocKeys.Install_LoadingLoaders);
        RefreshValidation();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var mcId = _minecraftVersionId;
            var settings = _settings;
            var loaderKind = loader;
            var list = await Task.Run(
                    () => _launchService.GetModLoaderVersionsAsync(
                        settings,
                        mcId,
                        loaderKind,
                        cts.Token),
                    cts.Token)
                .ConfigureAwait(true);

            if (generation != _loaderLoadGeneration)
                return;

            const int maxShown = 80;
            var shown = list.Count <= maxShown ? list : list.Take(maxShown).ToList();
            LoaderVersionBox.ItemsSource = shown;

            if (shown.Count == 0)
            {
                LoaderStatusText.Text = loader == ModLoaderKind.OptiFine
                    ? Loc.Get(LocKeys.Install_NoOptiFine)
                    : Loc.Get(LocKeys.Install_NoLoaders);
                _loaderVersionsReady = false;
            }
            else
            {
                var pick = PickDefault(shown);
                LoaderVersionBox.SelectedItem = pick;
                SuggestVersionNameForLoader(loader, pick?.Id);
                LoaderStatusText.Text = loader == ModLoaderKind.OptiFine
                    ? Loc.Format(LocKeys.Install_OptiFineCount, list.Count)
                    : Loc.Format(LocKeys.Install_LoaderCount, list.Count);
                _loaderVersionsReady = true;
                LoaderVersionBox.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            if (generation != _loaderLoadGeneration)
                return;

            var message = ex is OperationCanceledException
                ? Loc.Get(LocKeys.Error_TimedOut)
                : (string.IsNullOrWhiteSpace(ex.Message) ? Loc.Get(LocKeys.Error_Unknown) : ex.Message);

            LoaderVersionBox.ItemsSource = null;
            _loaderVersionsReady = false;
            LoaderStatusText.Text = loader == ModLoaderKind.OptiFine
                ? Loc.Format(LocKeys.Install_OptiFineLoadFailed, message)
                : Loc.Format(LocKeys.Install_LoaderLoadFailed, message);
        }

        RefreshValidation();
    }

    private static ModLoaderVersionOption? PickDefault(IReadOnlyList<ModLoaderVersionOption> list) =>
        list.FirstOrDefault(v => v.IsRecommended)
        ?? list.FirstOrDefault(v => v.IsLatest)
        ?? list.FirstOrDefault(v => v.IsStable)
        ?? list.FirstOrDefault();

    private ModLoaderKind SelectedLoader()
    {
        if (LoaderFabric.IsChecked == true)
            return ModLoaderKind.Fabric;
        if (LoaderForge.IsChecked == true)
            return ModLoaderKind.Forge;
        if (LoaderNeoForge.IsChecked == true)
            return ModLoaderKind.NeoForge;
        if (LoaderOptiFine.IsChecked == true)
            return ModLoaderKind.OptiFine;
        return ModLoaderKind.None;
    }

    private void RefreshValidation()
    {
        var name = VersionNameBox.Text ?? string.Empty;
        var error = NameRules.ValidateVersionName(name);
        var loader = SelectedLoader();
        var mcRoot = Path.GetDirectoryName(_versionsRoot);

        // Dependency parents (hidden Forge/Fabric bases) can be "claimed" as vanilla 1.21.11.
        var nameTakenByUserInstance =
            _existingNames.Contains(name) &&
            !GamePaths.IsDependencyOnly(name, mcRoot);

        if (error is null && nameTakenByUserInstance)
            error = Loc.Get(LocKeys.Validate_VersionExists);

        if (error is null && loader != ModLoaderKind.None)
        {
            if (string.Equals(name.Trim(), _minecraftVersionId, StringComparison.OrdinalIgnoreCase))
                error = Loc.Get(LocKeys.Validate_LoaderNameEqualsMc);
            else if (!_loaderVersionsReady || LoaderVersionBox.SelectedItem is null)
            {
                error = loader == ModLoaderKind.OptiFine
                    ? Loc.Get(LocKeys.Install_SelectOptiFineVersion)
                    : Loc.Get(LocKeys.Install_SelectLoaderVersion);
            }
            else
            {
                error = AddonConflictRules.ValidateInstall(loader);
            }
        }

        ErrorText.Text = error ?? string.Empty;
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        IsValid = error is null;
        ValidityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HashSet<string> LoadExistingNames(string versionsRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(versionsRoot))
                return set;

            foreach (var dir in Directory.EnumerateDirectories(versionsRoot))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                    set.Add(name);
            }
        }
        catch
        {
            // ignore
        }

        return set;
    }
}
