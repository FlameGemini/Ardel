namespace Ardel.Launcher.Helpers;

/// <summary>
/// Numeric Minecraft version ordering (1.21.11 &gt; 1.21.2, 26.1 &gt; 1.21.x).
/// String/ordinal sorts put 1.21.2 above 1.21.11 and scramble 26.x.
/// </summary>
public sealed class MinecraftVersionOrder : IComparer<string>
{
    public static MinecraftVersionOrder Ascending { get; } = new(descending: false);
    public static MinecraftVersionOrder Descending { get; } = new(descending: true);

    private readonly bool _descending;

    private MinecraftVersionOrder(bool descending) => _descending = descending;

    public int Compare(string? x, string? y)
    {
        var cmp = CompareAscending(x, y);
        return _descending ? -cmp : cmp;
    }

    /// <summary>Newer versions first.</summary>
    public static IOrderedEnumerable<string> OrderNewestFirst(IEnumerable<string> versions) =>
        versions.OrderByDescending(v => v, Ascending);

    private static int CompareAscending(string? x, string? y)
    {
        var ax = string.IsNullOrWhiteSpace(x);
        var ay = string.IsNullOrWhiteSpace(y);
        if (ax && ay)
            return 0;
        if (ax)
            return -1;
        if (ay)
            return 1;

        var left = x!.Trim();
        var right = y!.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return 0;

        var leftParsed = TryParse(left, out var leftParts, out var leftSuffix);
        var rightParsed = TryParse(right, out var rightParts, out var rightSuffix);

        // Recognized MC versions sort before tags like Client / Server / Java 21.
        if (leftParsed != rightParsed)
            return leftParsed ? -1 : 1;

        if (!leftParsed)
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        var n = Math.Max(leftParts.Count, rightParts.Count);
        for (var i = 0; i < n; i++)
        {
            var a = i < leftParts.Count ? leftParts[i] : 0;
            var b = i < rightParts.Count ? rightParts[i] : 0;
            if (a != b)
                return a.CompareTo(b);
        }

        return string.Compare(leftSuffix, rightSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses <c>1.21.11</c>, <c>26.1</c>, <c>26.1-rc1</c>. Rejects non-version labels.
    /// </summary>
    private static bool TryParse(string value, out List<int> parts, out string suffix)
    {
        parts = [];
        suffix = string.Empty;

        // Snapshot ids like 25w14a are not dotted releases — keep stable ordinal among themselves.
        if (value.Length > 0 && char.IsDigit(value[0]) && value.Contains('w', StringComparison.OrdinalIgnoreCase))
            return false;

        var core = value;
        var dash = value.IndexOf('-');
        if (dash > 0)
        {
            core = value[..dash];
            suffix = value[(dash + 1)..];
        }

        var segments = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        foreach (var segment in segments)
        {
            if (!TryParseSegment(segment, out var number))
                return false;
            parts.Add(number);
        }

        // Real MC releases are 2–4 numeric components (26.1 / 1.21.11 / 1.7.10).
        return parts.Count is >= 2 and <= 4;
    }

    private static bool TryParseSegment(string segment, out int number)
    {
        number = 0;
        if (segment.Length == 0)
            return false;

        // Allow a trailing letter on a component (rare); digits must lead.
        var end = 0;
        while (end < segment.Length && char.IsAsciiDigit(segment[end]))
            end++;

        if (end == 0)
            return false;

        // Reject "Java21"-style tokens that somehow start with digits only partially.
        if (end < segment.Length && !char.IsAsciiLetter(segment[end]))
            return false;

        return int.TryParse(segment.AsSpan(0, end), out number);
    }
}
