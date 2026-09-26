using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

internal static class BmclUsageResolver
{
    public static MirrorPipeline Resolve(LauncherSettings settings)
    {
        if (settings is null || !settings.UseBmclApi)
            return MirrorPipeline.Official;

        return settings.BmclUsageMode == (int)BmclUsageMode.Always
            ? MirrorPipeline.AlwaysBmcl
            : MirrorPipeline.FallbackWhenSlow;
    }
}

internal enum MirrorPipeline
{
    Official,
    AlwaysBmcl,
    FallbackWhenSlow
}
