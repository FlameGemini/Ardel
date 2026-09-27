using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace Ardel.Launcher.Helpers;

public static class DialogHelper
{
    private static readonly SemaphoreSlim _dialogLock = new(1, 1);

    /// <summary>
    /// Safely shows a ContentDialog by ensuring only one dialog is active at a time
    /// and preventing unhandled WinUI 3 concurrency COMExceptions (0x80000019).
    /// </summary>
    public static async Task<ContentDialogResult> SafeShowAsync(this ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (dialog.XamlRoot is null)
        {
            dialog.XamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
            if (dialog.XamlRoot is null)
                return ContentDialogResult.None;
        }

        await _dialogLock.WaitAsync().ConfigureAwait(true);
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DialogHelper] SafeShowAsync caught: {ex.Message}");
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
