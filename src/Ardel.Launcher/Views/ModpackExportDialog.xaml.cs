using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Views;

public sealed partial class ModpackExportDialog : UserControl
{
    public ModpackExportDialog()
    {
        InitializeComponent();
    }

    public ModpackExportOptions BuildOptions() => new()
    {
        IncludeMods = ModsCheck.IsChecked == true,
        IncludeConfig = ConfigCheck.IsChecked == true,
        IncludeResourcePacks = ResourcePacksCheck.IsChecked == true,
        IncludeShaderPacks = ShaderPacksCheck.IsChecked == true,
        IncludeDatapacks = DatapacksCheck.IsChecked == true,
        IncludeOptions = OptionsCheck.IsChecked == true,
        IncludeSaves = SavesCheck.IsChecked == true,
        IncludeScreenshots = ScreenshotsCheck.IsChecked == true,
        PreferThinPack = ThinPackCheck.IsChecked == true
    };

    public static async Task<ModpackExportOptions?> ShowAsync(XamlRoot xamlRoot)
    {
        await Task.Yield();
        var content = new ModpackExportDialog();
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Modpack_ExportDialogTitle),
            PrimaryButtonText = Loc.Get(LocKeys.InstanceSettings_Export),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? content.BuildOptions() : null;
    }
}
