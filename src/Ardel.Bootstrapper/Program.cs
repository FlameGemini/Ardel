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
            if (string.IsNullOrEmpty(localAppData))
                localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            string baseRuntimeDir = Path.Combine(localAppData, AppName, "runtime");

            // 3. Obtain pre-computed payload hash (instant < 0.1ms)
            var assembly = Assembly.GetExecutingAssembly();
            string payloadHash = string.Empty;
            string? hashResourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.hash", StringComparison.OrdinalIgnoreCase));

            if (hashResourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(hashResourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    payloadHash = (reader.ReadToEnd() ?? string.Empty).Trim().ToLowerInvariant();
                }
            }

            string? zipResourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(payloadHash))
            {
                if (zipResourceName == null)
                {
                    // Fallback for development / side-by-side execution
                    string directExe = Path.Combine(launchDir, "Ardel.Launcher.exe");
                    if (File.Exists(directExe))
                    {
                        return Launch(directExe, launchDir, args);
                    }
                    return 1;
                }

                // Compute payload hash only if hash was not pre-embedded
                using var stream = assembly.GetManifestResourceStream(zipResourceName)!;
                using var sha = SHA256.Create();
                byte[] hashBytes = sha.ComputeHash(stream);
                payloadHash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
            }

            string appRuntimeDir = Path.Combine(baseRuntimeDir, payloadHash);
            string completeMarker = Path.Combine(appRuntimeDir, ".complete");
            string targetExe = Path.Combine(appRuntimeDir, "Ardel.Launcher.exe");

            // Ultra-Fast Path: If already extracted and verified, launch immediately with ZERO delay (< 1ms)
            if (File.Exists(completeMarker) && File.Exists(targetExe))
            {
                return Launch(targetExe, launchDir, args);
            }

            // Extraction Path (First run or after updates)
            Directory.CreateDirectory(baseRuntimeDir);
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

                    if (zipResourceName != null)
                    {
                        using var stream = assembly.GetManifestResourceStream(zipResourceName)!;
                        byte[] zipBytes = new byte[stream.Length];
                        int totalRead = 0;
                        while (totalRead < zipBytes.Length)
                        {
                            int read = stream.Read(zipBytes, totalRead, zipBytes.Length - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        // Gather all entry names upfront
                        List<string> entryNames;
                        using (var probeArchive = new ZipArchive(new MemoryStream(zipBytes, false), ZipArchiveMode.Read))
                        {
                            entryNames = probeArchive.Entries.Select(e => e.FullName).ToList();
                        }

                        // Pre-create all directories upfront in batch to eliminate filesystem lock contention
                        var uniqueDirs = entryNames
                            .Select(e => Path.GetDirectoryName(Path.Combine(appRuntimeDir, e)))
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Distinct(StringComparer.OrdinalIgnoreCase);

                        foreach (var dir in uniqueDirs)
                        {
                            if (dir != null && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                        }

                        // Extract files concurrently across all CPU cores
                        int maxParallelism = Math.Clamp(Environment.ProcessorCount, 2, 16);
                        Parallel.ForEach(
                            entryNames,
                            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                            () => new ZipArchive(new MemoryStream(zipBytes, false), ZipArchiveMode.Read),
                            (entryName, loopState, localArchive) =>
                            {
                                var entry = localArchive.GetEntry(entryName);
                                if (entry == null || string.IsNullOrEmpty(entry.Name))
                                    return localArchive;

                                string destPath = Path.Combine(appRuntimeDir, entry.FullName);
                                using var entryStream = entry.Open();
                                using var destStream = new FileStream(
                                    destPath,
                                    FileMode.Create,
                                    FileAccess.Write,
                                    FileShare.None,
                                    bufferSize: 128 * 1024,
                                    options: FileOptions.SequentialScan);
                                entryStream.CopyTo(destStream, 128 * 1024);

                                return localArchive;
                            },
                            localArchive => localArchive.Dispose()
                        );
                    }

                    File.WriteAllText(completeMarker, payloadHash);
                }

                // Clean up stale runtime payload directories to prevent disk clutter
                try
                {
                    if (Directory.Exists(baseRuntimeDir))
                    {
                        foreach (var dir in Directory.GetDirectories(baseRuntimeDir))
                        {
                            var dirName = Path.GetFileName(dir);
                            if (!string.Equals(dirName, payloadHash, StringComparison.OrdinalIgnoreCase))
                            {
                                try { Directory.Delete(dir, true); } catch { }
                            }
                        }
                    }
                }
                catch { }
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
