using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Ardel.Bootstrapper;

internal static class Program
{
    private const string AppName = "Ardel";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // 1. Determine portable working directory where Ardel.exe was executed
            string launchDir = AppDomain.CurrentDomain.BaseDirectory;
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrEmpty(dir))
                    launchDir = dir;
            }
            launchDir = Path.GetFullPath(launchDir);

            // 2. Determine target extracted runtime directory in LocalAppData
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string baseRuntimeDir = Path.Combine(localAppData, AppName, "runtime");
            Directory.CreateDirectory(baseRuntimeDir);

            // 3. Locate embedded payload
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                // Fallback for development / side-by-side execution
                string directExe = Path.Combine(launchDir, "Ardel.Launcher.exe");
                if (File.Exists(directExe))
                {
                    return Launch(directExe, launchDir, args);
                }
                return 1;
            }

            // Compute payload hash to manage versioning and updates cleanly
            string payloadHash;
            using (var stream = assembly.GetManifestResourceStream(resourceName)!)
            using (var sha = SHA256.Create())
            {
                byte[] hashBytes = sha.ComputeHash(stream);
                payloadHash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
            }

            string appRuntimeDir = Path.Combine(baseRuntimeDir, payloadHash);
            string completeMarker = Path.Combine(appRuntimeDir, ".complete");
            string targetExe = Path.Combine(appRuntimeDir, "Ardel.Launcher.exe");

            using var extractMutex = new Mutex(false, @"Global\Ardel_Bootstrapper_Extract_" + payloadHash);
            var mutexAcquired = false;
            try
            {
                try
                {
                    mutexAcquired = extractMutex.WaitOne(TimeSpan.FromSeconds(30), false);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!File.Exists(completeMarker) || !File.Exists(targetExe))
                {
                    if (Directory.Exists(appRuntimeDir))
                    {
                        try { Directory.Delete(appRuntimeDir, true); } catch { }
                    }
                    Directory.CreateDirectory(appRuntimeDir);

                    using (var stream = assembly.GetManifestResourceStream(resourceName)!)
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        archive.ExtractToDirectory(appRuntimeDir, overwriteFiles: true);
                    }

                    File.WriteAllText(completeMarker, payloadHash);
                }
            }
            finally
            {
                if (mutexAcquired)
                {
                    try { extractMutex.ReleaseMutex(); } catch { }
                }
            }

            // 4. Launch Ardel.Launcher.exe with ARDEL_PORTABLE_ROOT set to where Ardel.exe is placed
            return Launch(targetExe, launchDir, args);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Bootstrapper error: " + ex);
            try
            {
                MessageBox(IntPtr.Zero, "Failed to start Ardel:\n" + ex.ToString(), "Ardel Launcher Error", 0x10);
            }
            catch { }
            return 1;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static int Launch(string exePath, string workingDir, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };

        psi.EnvironmentVariables["ARDEL_PORTABLE_ROOT"] = workingDir;
        string? bootstrapperExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(bootstrapperExe))
            bootstrapperExe = Path.Combine(workingDir, "Ardel.exe");
        psi.EnvironmentVariables["ARDEL_BOOTSTRAPPER_EXE"] = bootstrapperExe;
        psi.EnvironmentVariables["ARDEL_BOOTSTRAPPER_PID"] = Environment.ProcessId.ToString();

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null) return 1;

        process.WaitForExit();
        return process.ExitCode;
    }
}
