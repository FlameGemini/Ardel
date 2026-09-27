using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Views;

public sealed partial class ModInstallDialog : UserControl
{
    private readonly ModFileVersionItem _file;
    private readonly string _sourceId;
    private readonly string _minecraftRoot;
    private readonly CatalogProjectKind _kind;
    private string _fileName;
    private GameVersionItem? _pendingInstance;
    private TaskCompletionSource<MissingDependencyChoice>? _depPromptTcs;

    public event EventHandler<ModFileInstallRequest>? InstallRequested;
    public event EventHandler<IReadOnlyList<ModFileInstallRequest>>? InstallBatchRequested;

    public ModInstallDialog(
        ModProjectDetail project,
        ModFileVersionItem file,
        IReadOnlyList<GameVersionItem> instances,
        string minecraftRoot,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        CatalogProjectKind kind = CatalogProjectKind.Mod)
    {
        _file = file;
        _sourceId = project.SourceId;
        _minecraftRoot = minecraftRoot;
        _kind = kind;
        _fileName = SanitizeFileName(file.FileName);

        InitializeComponent();

        TitleText.Text = Loc.Format(LocKeys.Mod_InstallTitle, file.DisplayName);
        SubtitleText.Text = Loc.Format(LocKeys.Mod_InstallSubtitle, project.Title);
        FileNameBox.Text = _fileName;
        FileNameBox.PlaceholderText = file.FileName;
        DependencyPromptTitle.Text = Loc.Get(LocKeys.Mod_InstallMissingDepsTitle);

        if (kind == CatalogProjectKind.Datapack)
        {
            DatapackTargetPanel.Visibility = Visibility.Visible;
            UpdateDatapackTargetHint();
        }

        var groups = ModInstanceMatcher.BuildGroups(
            instances,
            file,
            minecraftRoot,
            preferredGameVersion,
            preferredLoaderSlug,
            kind);

        EmptyText.Text = CatalogCopy.NoCompatible(kind, instances.Count > 0);
        EmptyText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstanceList.Visibility = groups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var cvs = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = groups
        };
        InstanceList.ItemsSource = cvs.View;
    }

    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        ModProjectDetail project,
        ModFileVersionItem file,
        IReadOnlyList<GameVersionItem> instances,
        string minecraftRoot,
        string? preferredGameVersion,
        string? preferredLoaderSlug,
        Action<ModFileInstallRequest> onInstall,
        Action<IReadOnlyList<ModFileInstallRequest>>? onInstallBatch = null,
        CatalogProjectKind kind = CatalogProjectKind.Mod)
    {
        var content = new ModInstallDialog(
            project,
            file,
            instances,
            minecraftRoot,
            preferredGameVersion,
            preferredLoaderSlug,
            kind);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = CatalogCopy.InstallDialogTitle(kind),
            Content = content,
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };

        content.InstallRequested += (_, request) =>
        {
            onInstall(request);
            dialog.Hide();
        };

        content.InstallBatchRequested += (_, requests) =>
        {
            if (onInstallBatch is not null)
                onInstallBatch(requests);
            else
            {
                foreach (var request in requests)
                    onInstall(request);
            }
            dialog.Hide();
        };

        await dialog.SafeShowAsync();
    }

    private void DatapackTarget_Checked(object sender, RoutedEventArgs e) =>
        UpdateDatapackTargetHint();

    private void UpdateDatapackTargetHint()
    {
        if (_kind != CatalogProjectKind.Datapack)
            return;

        DatapackTargetHint.Text = WorldDatapackRadio.IsChecked == true
            ? Loc.Get(LocKeys.Catalog_DatapackTargetWorldHint)
            : Loc.Get(LocKeys.Catalog_DatapackTargetInstanceHint);
    }

    private void FileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = FileNameBox.Text?.Trim() ?? string.Empty;
        _fileName = string.IsNullOrWhiteSpace(text)
            ? SanitizeFileName(_file.FileName)
            : SanitizeFileName(text);
    }

    private void InstanceList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GameVersionItem instance)
            return;

        if (_kind == CatalogProjectKind.Datapack && WorldDatapackRadio.IsChecked == true)
        {
            _pendingInstance = instance;
            ShowWorldPicker(instance);
            return;
        }

        _ = InstallToInstanceAsync(instance, targetWorld: null);
    }

    private void ShowWorldPicker(GameVersionItem instance)
    {
        var instanceDir = GamePaths.EnsureVersionIsolation(instance.Id, _minecraftRoot);
        var savesDir = Path.Combine(instanceDir, "saves");
        var worlds = new List<string>();
        if (Directory.Exists(savesDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(savesDir))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrWhiteSpace(name))
                    worlds.Add(name);
            }

            worlds.Sort(StringComparer.OrdinalIgnoreCase);
        }

        WorldPanelTitle.Text = Loc.Format(LocKeys.Catalog_DatapackPickWorld, instance.Id);
        WorldList.ItemsSource = worlds;
        WorldEmptyText.Visibility = worlds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WorldList.Visibility = worlds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        InstanceList.Visibility = Visibility.Collapsed;
        WorldPanel.Visibility = Visibility.Visible;
    }

    private void BackToInstances_Click(object sender, RoutedEventArgs e)
    {
        _pendingInstance = null;
        WorldPanel.Visibility = Visibility.Collapsed;
        InstanceList.Visibility = Visibility.Visible;
    }

    private void WorldList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_pendingInstance is null || e.ClickedItem is not string worldName)
            return;

        _ = InstallToInstanceAsync(_pendingInstance, worldName);
    }

    private async Task InstallToInstanceAsync(GameVersionItem instance, string? targetWorld)
    {
        var fileName = string.IsNullOrWhiteSpace(_fileName)
            ? SanitizeFileName(_file.FileName)
            : _fileName;
        var defaultExt = _kind == CatalogProjectKind.Mod ? ".jar" : ".zip";
        if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            fileName += defaultExt;

        var instanceDir = GamePaths.EnsureVersionIsolation(instance.Id, _minecraftRoot);
        string targetDir;
        if (_kind == CatalogProjectKind.Datapack && !string.IsNullOrWhiteSpace(targetWorld))
        {
            targetDir = Path.Combine(instanceDir, "saves", targetWorld.Trim(), "datapacks");
        }
        else
        {
            var folder = _kind switch
            {
                CatalogProjectKind.ResourcePack => "resourcepacks",
                CatalogProjectKind.Datapack => "datapacks",
                CatalogProjectKind.ShaderPack => "shaderpacks",
                CatalogProjectKind.Modpack => "modpacks",
                _ => "mods"
            };
            targetDir = Path.Combine(instanceDir, folder);
        }

        Directory.CreateDirectory(targetDir);

        if (_kind == CatalogProjectKind.Mod && string.IsNullOrWhiteSpace(targetWorld) &&
            ModDependencyInstallService.HasDependencyCandidates(_file))
        {
            SetDependencyCheckBusy(true);
            try
            {
                var depService = new ModDependencyInstallService();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var missing = await Task.Run(async () =>
                        await depService.GetMissingDependenciesAsync(
                            instance,
                            _file,
                            _sourceId,
                            _minecraftRoot,
                            cts.Token).ConfigureAwait(false))
                    .ConfigureAwait(true);

                if (missing.MissingRefs.Count > 0)
                {
                    SetDependencyCheckBusy(false);
                    var choice = await ShowDependencyPromptAsync(missing.Missing).ConfigureAwait(true);
                    if (choice == MissingDependencyChoice.Cancel)
                        return;

                    if (choice == MissingDependencyChoice.InstallAll)
                    {
                        SetDependencyCheckBusy(true);
                        try
                        {
                            var batch = await Task.Run(async () =>
                                    await depService.BuildInstallRequestsAsync(
                                        instance,
                                        _file,
                                        fileName,
                                        _minecraftRoot,
                                        includeDependencies: true,
                                        missing.MissingRefs,
                                        CancellationToken.None).ConfigureAwait(false))
                                .ConfigureAwait(true);

                            if (batch.Count > 0)
                            {
                                InstallBatchRequested?.Invoke(this, batch);
                                return;
                            }
                        }
                        finally
                        {
                            SetDependencyCheckBusy(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModInstallDialog] dependency check failed: {ex.Message}");
            }
            finally
            {
                SetDependencyCheckBusy(false);
            }
        }

        var displayName = string.IsNullOrWhiteSpace(targetWorld)
            ? Loc.Format(LocKeys.Mod_InstallJobName, _file.DisplayName, instance.Id)
            : Loc.Format(LocKeys.Catalog_DatapackInstallJobName, _file.DisplayName, instance.Id, targetWorld);

        InstallRequested?.Invoke(this, new ModFileInstallRequest
        {
            DisplayName = displayName,
            FileName = fileName,
            DownloadUrl = _file.DownloadUrl,
            TargetInstanceId = instance.Id,
            ModsDirectory = targetDir,
            TargetWorldName = targetWorld
        });
    }

    private void SetDependencyCheckBusy(bool busy)
    {
        DependencyCheckOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InstanceList.IsEnabled = !busy;
        WorldList.IsEnabled = !busy;
        FileNameBox.IsEnabled = !busy;
    }

    private enum MissingDependencyChoice
    {
        Cancel,
        ModOnly,
        InstallAll
    }

    private Task<MissingDependencyChoice> ShowDependencyPromptAsync(IReadOnlyList<ModDependencyItem> missing)
    {
        _depPromptTcs = new TaskCompletionSource<MissingDependencyChoice>();
        DependencyPromptList.ItemsSource = missing;
        DependencyPromptBody.Text = Loc.Format(LocKeys.Mod_InstallMissingDepsBody, string.Empty, missing.Count);
        InstallAllDepsButton.Content = Loc.Format(LocKeys.Mod_InstallWithDependencies, missing.Count);

        PickInstanceHint.Visibility = Visibility.Collapsed;
        FileNameBox.Visibility = Visibility.Collapsed;
        InstanceList.Visibility = Visibility.Collapsed;
        WorldPanel.Visibility = Visibility.Collapsed;
        DependencyPromptPanel.Visibility = Visibility.Visible;

        return _depPromptTcs.Task;
    }

    private void HideDependencyPrompt()
    {
        DependencyPromptPanel.Visibility = Visibility.Collapsed;
        PickInstanceHint.Visibility = Visibility.Visible;
        FileNameBox.Visibility = Visibility.Visible;
        InstanceList.Visibility = Visibility.Visible;
    }

    private void CompleteDependencyPrompt(MissingDependencyChoice choice)
    {
        HideDependencyPrompt();
        _depPromptTcs?.TrySetResult(choice);
        _depPromptTcs = null;
    }

    private void DependencyPromptBack_Click(object sender, RoutedEventArgs e) =>
        CompleteDependencyPrompt(MissingDependencyChoice.Cancel);

    private void InstallModOnlyButton_Click(object sender, RoutedEventArgs e) =>
        CompleteDependencyPrompt(MissingDependencyChoice.ModOnly);

    private void InstallAllDepsButton_Click(object sender, RoutedEventArgs e) =>
        CompleteDependencyPrompt(MissingDependencyChoice.InstallAll);

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return string.IsNullOrWhiteSpace(trimmed) ? "pack.zip" : trimmed;
    }
}
