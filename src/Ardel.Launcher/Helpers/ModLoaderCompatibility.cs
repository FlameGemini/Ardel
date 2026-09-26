namespace Ardel.Launcher.Helpers;

/// <summary>Compatibility checks for Mod catalog filters.</summary>
internal static class ModLoaderCompatibility
{
    public const string LiteLoaderId = "2";

    /// <summary>
    /// LiteLoader Legacy targets Minecraft 1.5.2–1.12.2.
    /// Empty / "any" version is treated as compatible (no version filter).
    /// </summary>
    public static bool IsLiteLoaderCompatible(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
            return true;

        var text = gameVersion.Trim();
        if (!text.StartsWith("1.", StringComparison.Ordinal))
            return false;

        var rest = text.AsSpan(2);
        var dot = rest.IndexOf('.');
        var minorSpan = dot < 0 ? rest : rest[..dot];

        // Stop at first non-digit (snapshots like 1.12-pre / weird suffixes).
        var digits = 0;
        while (digits < minorSpan.Length && char.IsAsciiDigit(minorSpan[digits]))
            digits++;
        if (digits == 0)
            return false;

        if (!int.TryParse(minorSpan[..digits], out var minor))
            return false;

        return minor is >= 5 and <= 12;
    }
}
