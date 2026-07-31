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
    private readonly string _minecraftRoot;
    private string _fileName;

    public event EventHandler<ModFileInstallRequest>? InstallRequested;

    public ModInstallDialog(
        ModProjectDetail project,
        ModFileVersionItem file,
        IReadOnlyList<GameVersionItem> instances,
        string minecraftRoot,
        string? preferredGameVersion,
        string? preferredLoaderSlug)
    {
        _file = file;
        _minecraftRoot = minecraftRoot;
        _fileName = SanitizeFileName(file.FileName);

        InitializeComponent();

        TitleText.Text = Loc.Format(LocKeys.Mod_InstallTitle, file.DisplayName);
        SubtitleText.Text = Loc.Format(LocKeys.Mod_InstallSubtitle, project.Title);
        FileNameBox.Text = _fileName;
        FileNameBox.PlaceholderText = file.FileName;

        var groups = ModInstanceMatcher.BuildGroups(
            instances,
            file,
            minecraftRoot,
            preferredGameVersion,
            preferredLoaderSlug);

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
        Action<ModFileInstallRequest> onInstall)
    {
        var content = new ModInstallDialog(
            project,
            file,
            instances,
            minecraftRoot,
            preferredGameVersion,
            preferredLoaderSlug);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Mod_InstallDialogTitle),
            Content = content,
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };

        content.InstallRequested += (_, request) =>
        {
            onInstall(request);
            dialog.Hide();
        };

        await dialog.ShowAsync();
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

        var fileName = string.IsNullOrWhiteSpace(_fileName)
            ? SanitizeFileName(_file.FileName)
            : _fileName;
        if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            fileName += ".jar";

        var instanceDir = GamePaths.EnsureVersionIsolation(instance.Id, _minecraftRoot);
        var modsDir = Path.Combine(instanceDir, "mods");
        Directory.CreateDirectory(modsDir);

        InstallRequested?.Invoke(this, new ModFileInstallRequest
        {
            DisplayName = Loc.Format(LocKeys.Mod_InstallJobName, _file.DisplayName, instance.Id),
            FileName = fileName,
            DownloadUrl = _file.DownloadUrl,
            TargetInstanceId = instance.Id,
            ModsDirectory = modsDir
        });
    }

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return string.IsNullOrWhiteSpace(trimmed) ? "mod.jar" : trimmed;
    }
}
