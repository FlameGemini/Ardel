namespace Ardel.Launcher.Helpers;

/// <summary>
/// Detects Mojang April Fools / joke clients from the version manifest.
/// </summary>
internal static class MinecraftAprilFools
{
    /// <summary>
    /// True for joke snapshots (and a few named oddities). Real releases that
    /// merely ship on April 1 are excluded.
    /// </summary>
    public static bool IsAprilFools(string? id, string? type, DateTimeOffset? releaseTime)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        if (MatchesKnownJokeId(id))
            return true;

        // Weekly snapshots that land on April 1 UTC are the usual joke drop.
        if (!string.Equals(type, "snapshot", StringComparison.OrdinalIgnoreCase))
            return false;

        if (releaseTime is not { } when)
            return false;

        var utc = when.ToUniversalTime();
        return utc.Month == 4 && utc.Day == 1;
    }

    private static bool MatchesKnownJokeId(string id)
    {
        // Substring matches for distinctive joke ids.
        ReadOnlySpan<string> needles =
        [
            "potato",
            "infinite",
            "craftmine",
            "or_b",
            "oneblockatatime",
            "shareware",
            "rv-pre",
            "pointless"
        ];

        foreach (var needle in needles)
        {
            if (id.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return id.Equals("15w14a", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("2.0", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("3D Shareware v1.34", StringComparison.OrdinalIgnoreCase);
    }
}
