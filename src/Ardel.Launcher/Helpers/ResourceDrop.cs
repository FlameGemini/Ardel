using Windows.Storage;
using Windows.Storage.Pickers;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Helpers;

internal static class ResourceDrop
{
    public static bool IsModFileName(string name) =>
        name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    public static bool IsPackZipName(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsAccepted(IStorageItem item, bool modsOnly) =>
        modsOnly
            ? item is StorageFile file && IsModFileName(file.Name)
            : item is StorageFolder || (item is StorageFile zip && IsPackZipName(zip.Name));

    public static string FormatSize(long length) =>
        length > 1024 * 1024
            ? $"{length / (1024.0 * 1024.0):F1} MB"
            : $"{length / 1024.0:F1} KB";

    public static long DirectorySize(string path)
    {
        long n = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { n += new FileInfo(file).Length; }
                catch { /* skip locked files */ }
            }
        }
        catch
        {
            // Unreadable tree.
        }

        return n;
    }

    public static FileOpenPicker CreatePicker(bool modsOnly, nint hwnd)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        if (modsOnly)
            picker.FileTypeFilter.Add(".jar");
        else
            picker.FileTypeFilter.Add(".zip");

        picker.CommitButtonText = Loc.Get(LocKeys.Action_Browse);
        return picker;
    }

    public static async Task CopyItemsAsync(
        IReadOnlyList<IStorageItem> items,
        string destDir,
        bool modsOnly,
        Func<string, Task<bool>> confirmOverwrite,
        Action<string> reportStatus)
    {
        Directory.CreateDirectory(destDir);
        var destFolder = await StorageFolder.GetFolderFromPathAsync(destDir);
        var accepted = items.Where(i => IsAccepted(i, modsOnly)).ToList();
        if (accepted.Count == 0)
        {
            reportStatus(Loc.Get(LocKeys.InstanceSettings_DropRejected));
            return;
        }

        foreach (var item in accepted)
        {
            try
            {
                var destPath = Path.Combine(destDir, item.Name);
                var exists = File.Exists(destPath) || Directory.Exists(destPath);
                if (exists && !await confirmOverwrite(item.Name).ConfigureAwait(true))
                    continue;

                if (item is StorageFile file)
                {
                    await file.CopyAsync(destFolder, file.Name, NameCollisionOption.ReplaceExisting);
                }
                else if (item is StorageFolder folder)
                {
                    var dest = await destFolder.CreateFolderAsync(
                        folder.Name, CreationCollisionOption.ReplaceExisting);
                    await CopyFolderAsync(folder, dest).ConfigureAwait(true);
                }

                reportStatus(Loc.Format(LocKeys.InstanceSettings_DropAdded, item.Name));
            }
            catch (Exception ex)
            {
                reportStatus(Loc.Format(LocKeys.InstanceSettings_DropFailed, item.Name, ex.Message));
            }
        }
    }

    private static async Task CopyFolderAsync(StorageFolder source, StorageFolder dest)
    {
        foreach (var file in await source.GetFilesAsync())
            await file.CopyAsync(dest, file.Name, NameCollisionOption.ReplaceExisting);

        foreach (var child in await source.GetFoldersAsync())
        {
            var next = await dest.CreateFolderAsync(child.Name, CreationCollisionOption.OpenIfExists);
            await CopyFolderAsync(child, next);
        }
    }
}
