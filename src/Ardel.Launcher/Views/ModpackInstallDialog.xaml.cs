using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

/// <summary>Name a new instance for a catalog modpack install.</summary>
public sealed partial class ModpackInstallDialog : UserControl
{
    private readonly ModProjectDetail _project;
    private readonly ModFileVersionItem _file;
    private readonly string _versionsRoot;
    private readonly string _sourceId;

    public ModpackInstallDialog(
        ModProjectDetail project,
        ModFileVersionItem file,
        string versionsRoot)
    {
        _project = project;
        _file = file;
        _versionsRoot = versionsRoot;
        _sourceId = project.SourceId;

        InitializeComponent();
        TitleText.Text = Loc.Format(LocKeys.Modpack_InstallTitle, project.Title);
        SubtitleText.Text = Loc.Format(LocKeys.Modpack_InstallSubtitle, file.DisplayName);
        InstanceNameBox.Text = ModpackInstallService.SuggestInstanceName(project.Title, versionsRoot);
        InstanceNameBox.TextChanged += (_, _) => RefreshValidation();
        RefreshValidation();
    }

    public bool IsValid { get; private set; }

    public event EventHandler? ValidityChanged;

    public ModpackInstallRequest? BuildRequest()
    {
        if (!IsValid)
            return null;

        return new ModpackInstallRequest
        {
            DisplayName = Loc.Format(LocKeys.Modpack_JobName, _project.Title, InstanceNameBox.Text.Trim()),
            PackDownloadUrl = _file.DownloadUrl,
            SourceId = _sourceId,
            InstanceName = InstanceNameBox.Text.Trim(),
            PackTitle = _project.Title
        };
    }

    private void RefreshValidation()
    {
        var error = NameRules.ValidateVersionName(InstanceNameBox.Text, _versionsRoot);
        ValidationText.Text = error ?? string.Empty;
        var valid = error is null;
        if (IsValid == valid)
            return;
        IsValid = valid;
        ValidityChanged?.Invoke(this, EventArgs.Empty);
    }

    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        ModProjectDetail project,
        ModFileVersionItem file,
        string gameDirectory,
        Action<ModpackInstallRequest> onInstall)
    {
        await Task.Yield();

        var versionsRoot = GamePaths.GetVersionsRoot(gameDirectory);
        var content = new ModpackInstallDialog(project, file, versionsRoot);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Modpack_DialogTitle),
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
            if (result != ContentDialogResult.Primary)
                return;

            var request = content.BuildRequest();
            if (request is not null)
                onInstall(request);
        }
        finally
        {
            content.ValidityChanged -= OnValidity;
        }
    }
}
