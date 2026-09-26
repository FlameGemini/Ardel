using System.Diagnostics;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Dynamic heap sizing from free physical RAM and instance mod pressure.
/// Target tiers rise with the mods folder; free RAM is claimed in stages
/// (full claim for lower tiers, shrinking fractions for higher ones).
/// </summary>
internal static class DynamicMemoryAllocator
{
    private const int AbsoluteMinMaxMb = 512;
    private const int AbsoluteCapMb = 32768;

    public static (int MaxMb, int MinMb) Allocate(string? instanceDirectory = null)
    {
        if (!SystemMemory.TryGet(out _, out var availableBytes))
            return (4096, 2048);

        // Available RAM in GiB, one decimal (same granularity as common launcher UIs).
        var availableGiB = Math.Round(availableBytes / 1024.0 / 1024.0 / 1024.0 * 10.0) / 10.0;

        var profile = ResolveProfile(instanceDirectory);
        var giveGiB = ClaimFromAvailable(availableGiB, profile);
        giveGiB = Math.Round(Math.Max(giveGiB, profile.MinimumGiB), 1);

        if (SystemMemory.TryGet(out var totalBytes, out _))
        {
            var totalGiB = totalBytes / 1024.0 / 1024.0 / 1024.0;
            // Soft ceiling: do not schedule more than ~75% of installed RAM.
            giveGiB = Math.Min(giveGiB, Math.Round(totalGiB * 0.75, 1));
        }

        var maxMb = AlignDown(
            Math.Clamp((int)Math.Round(giveGiB * 1024.0), AbsoluteMinMaxMb, AbsoluteCapMb),
            128);

        // Commit a solid floor so large packs do not thrash while the heap grows.
        var minRatio = profile.ModCount >= 200 ? 0.65 : profile.ModCount >= 50 ? 0.55 : 0.5;
        var minMb = AlignDown(Math.Clamp((int)(maxMb * minRatio), 512, maxMb), 128);
        return (maxMb, minMb);
    }

    private static Profile ResolveProfile(string? instanceDirectory)
    {
        var modCount = CountModsFolderEntries(instanceDirectory);
        if (modCount > 0 || LooksModLoaderInstance(instanceDirectory))
        {
            var n = (double)Math.Max(modCount, 0);
            return new Profile(
                ModCount: modCount,
                MinimumGiB: 0.5 + n / 150.0,
                Target1GiB: 1.5 + n / 90.0,
                Target2GiB: 2.7 + n / 50.0,
                Target3GiB: 4.5 + n / 25.0);
        }

        if (LooksOptiFineInstance(instanceDirectory))
        {
            return new Profile(
                ModCount: 0,
                MinimumGiB: 0.5,
                Target1GiB: 1.5,
                Target2GiB: 3.0,
                Target3GiB: 5.0);
        }

        return new Profile(
            ModCount: 0,
            MinimumGiB: 0.5,
            Target1GiB: 1.5,
            Target2GiB: 2.5,
            Target3GiB: 4.0);
    }

    private static double ClaimFromAvailable(double availableGiB, Profile profile)
    {
        var remaining = availableGiB;
        var give = 0.0;
        var stages = new (double Delta, double Ratio)[]
        {
            (profile.Target1GiB, 1.0),
            (profile.Target2GiB - profile.Target1GiB, 0.7),
            (profile.Target3GiB - profile.Target2GiB, 0.4),
            (profile.Target3GiB, 0.15)
        };

        foreach (var (delta, ratio) in stages)
        {
            if (delta <= 0 || remaining < 0.1)
                break;

            give += Math.Min(remaining * ratio, delta);
            remaining -= delta / ratio;
            if (remaining < 0.1)
                break;
        }

        return give;
    }

    /// <summary>Count files in mods/ (enabled + disabled markers still add loader pressure).</summary>
    private static int CountModsFolderEntries(string? instanceDirectory)
    {
        if (string.IsNullOrWhiteSpace(instanceDirectory))
            return 0;

        try
        {
            var modsDir = Path.Combine(instanceDirectory, "mods");
            if (!Directory.Exists(modsDir))
                return 0;

            return Directory.GetFiles(modsDir).Length;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DynamicMemory] mod count failed: {ex.Message}");
            return 0;
        }
    }

    private static bool LooksModLoaderInstance(string? instanceDirectory)
    {
        if (string.IsNullOrWhiteSpace(instanceDirectory))
            return false;

        try
        {
            // Parent versions/<id> often sits next to the isolated instance folder.
            var versionId = Path.GetFileName(instanceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var versionsRoot = Path.GetDirectoryName(instanceDirectory);
            if (string.IsNullOrWhiteSpace(versionId) || string.IsNullOrWhiteSpace(versionsRoot))
                return false;

            var jsonPath = Path.Combine(versionsRoot, versionId, versionId + ".json");
            if (!File.Exists(jsonPath))
                return false;

            var text = File.ReadAllText(jsonPath);
            return text.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("quilt", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("liteloader", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksOptiFineInstance(string? instanceDirectory)
    {
        if (string.IsNullOrWhiteSpace(instanceDirectory))
            return false;

        try
        {
            var versionId = Path.GetFileName(instanceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var versionsRoot = Path.GetDirectoryName(instanceDirectory);
            if (string.IsNullOrWhiteSpace(versionId) || string.IsNullOrWhiteSpace(versionsRoot))
                return false;

            var jsonPath = Path.Combine(versionsRoot, versionId, versionId + ".json");
            if (!File.Exists(jsonPath))
                return false;

            return File.ReadAllText(jsonPath).Contains("optifine", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int AlignDown(int value, int step)
    {
        if (step <= 1)
            return value;
        return Math.Max(step, value - (value % step));
    }

    private readonly record struct Profile(
        int ModCount,
        double MinimumGiB,
        double Target1GiB,
        double Target2GiB,
        double Target3GiB);
}
