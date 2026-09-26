using Ardel.Launcher.Models;

namespace Ardel.Launcher.Helpers;

public static class ModFilePicker
{
    public static ModFileVersionItem? PickBestCompatibleFile(
        IReadOnlyList<ModFileVersionItem> files,
        string mcVersion,
        string? loaderSlug)
    {
        bool Matches(ModFileVersionItem f)
        {
            var gameOk = f.GameVersions.Any(g =>
                string.Equals(g.Trim(), mcVersion, StringComparison.OrdinalIgnoreCase));
            if (!gameOk)
                return false;

            if (string.IsNullOrEmpty(loaderSlug))
                return true;

            foreach (var raw in f.Loaders)
            {
                var slug = raw.Trim().ToLowerInvariant();
                if (string.Equals(slug, loaderSlug, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (slug == "quilt" && loaderSlug == "fabric")
                    return true;
            }

            return f.Loaders.Count == 0;
        }

        return files
            .Where(Matches)
            .OrderBy(f => f.Channel switch
            {
                ModReleaseChannel.Release => 0,
                ModReleaseChannel.Beta => 1,
                _ => 2
            })
            .ThenByDescending(f => f.Published ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }
}
