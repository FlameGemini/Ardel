using Ardel.Launcher.Helpers;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Services;

/// <summary>Resolves effective JVM heap for an instance against launcher defaults.</summary>
internal static class MemoryLaunchResolver
{
    public readonly record struct Result(int MaxMb, int MinMb, bool IsDynamic);

    public static Result Resolve(
        LauncherSettings global,
        InstanceSettings instance,
        string? instanceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(instance);

        var mode = NormalizeInstanceMode(instance);
        if (mode == InstanceMemoryMode.FollowDefault)
        {
            return NormalizeDefaultMode(global) == DefaultMemoryMode.Dynamic
                ? FromDynamic(instanceDirectory)
                : FromCustom(global.MaxRamMb);
        }

        if (mode == InstanceMemoryMode.Dynamic)
            return FromDynamic(instanceDirectory);

        return FromCustom(instance.MaxRamMb > 0 ? instance.MaxRamMb : global.MaxRamMb);
    }

    /// <summary>Effective allocation for UI previews (same rules as launch).</summary>
    public static Result Preview(
        DefaultMemoryMode defaultMode,
        int globalMaxMb,
        string? instanceDirectory = null) =>
        defaultMode == DefaultMemoryMode.Dynamic
            ? FromDynamic(instanceDirectory)
            : FromCustom(globalMaxMb);

    public static Result Preview(
        InstanceMemoryMode instanceMode,
        DefaultMemoryMode defaultMode,
        int instanceMaxMb,
        int globalMaxMb,
        string? instanceDirectory = null)
    {
        if (instanceMode == InstanceMemoryMode.FollowDefault)
            return Preview(defaultMode, globalMaxMb, instanceDirectory);
        if (instanceMode == InstanceMemoryMode.Dynamic)
            return FromDynamic(instanceDirectory);
        return FromCustom(instanceMaxMb > 0 ? instanceMaxMb : globalMaxMb);
    }

    public static InstanceMemoryMode NormalizeInstanceMode(InstanceSettings settings)
    {
        if (settings.MemoryMode is >= 0 and <= 2)
            return (InstanceMemoryMode)settings.MemoryMode;

        return settings.OverrideMemory
            ? InstanceMemoryMode.Custom
            : InstanceMemoryMode.FollowDefault;
    }

    public static DefaultMemoryMode NormalizeDefaultMode(LauncherSettings settings) =>
        settings.MemoryMode == (int)DefaultMemoryMode.Dynamic
            ? DefaultMemoryMode.Dynamic
            : DefaultMemoryMode.Custom;

    private static Result FromDynamic(string? instanceDirectory)
    {
        var (max, min) = DynamicMemoryAllocator.Allocate(instanceDirectory);
        return new Result(max, min, IsDynamic: true);
    }

    private static Result FromCustom(int maxMb)
    {
        var max = Math.Clamp(maxMb, 512, 65536);
        return new Result(max, MinMb: 0, IsDynamic: false);
    }
}
