using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Safe deletion to Windows Recycle Bin via Shell SHFileOperation.
/// Falls back to permanent delete / Directory.Delete if Shell operation fails.
/// </summary>
public static class RecycleBinHelper
{
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    /// <summary>
    /// Delete a file or directory to Windows Recycle Bin.
    /// </summary>
    public static bool MoveToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return true;

        try
        {
            // SHFileOperation requires double null-terminated string
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = fullPath + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };

            int result = SHFileOperation(ref fileOp);
            if (result == 0 && !fileOp.fAnyOperationsAborted)
            {
                return true;
            }

            Debug.WriteLine($"[RecycleBinHelper] SHFileOperation failed with code: {result}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RecycleBinHelper] MoveToRecycleBin error for '{path}': {ex.Message}");
        }

        // Fallback
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RecycleBinHelper] Fallback delete error: {ex.Message}");
        }

        return false;
    }
}
