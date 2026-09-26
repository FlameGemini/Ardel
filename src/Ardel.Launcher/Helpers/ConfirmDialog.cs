using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Helpers;

internal static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(string title, string content, string? primary = null)
    {
        var root = App.MainWindowInstance?.Content?.XamlRoot;
        if (root is null)
            return false;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            PrimaryButtonText = primary ?? Loc.Get(LocKeys.Action_Delete),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Task<bool> ConfirmDeleteAsync(IReadOnlyList<string> names)
    {
        if (names.Count == 1)
        {
            return ShowAsync(
                Loc.Get(LocKeys.Action_Delete),
                Loc.Format(LocKeys.InstanceSettings_DeleteConfirm, names[0]));
        }

        return ShowAsync(
            Loc.Get(LocKeys.Action_Delete),
            Loc.Format(LocKeys.InstanceSettings_DeleteConfirmMany, names.Count));
    }

    public static Task<bool> ConfirmOverwriteAsync(string fileName) =>
        ShowAsync(
            Loc.Get(LocKeys.Action_Apply),
            Loc.Format(LocKeys.InstanceSettings_OverwriteConfirm, fileName),
            Loc.Get(LocKeys.Action_Apply));
}
