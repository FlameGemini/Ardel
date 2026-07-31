using System.Text.RegularExpressions;
using Ardel.Launcher.Localization;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Validation rules for version folder names and offline player names.
/// </summary>
public static partial class NameRules
{
    private const int VersionNameMaxLength = 100;
    private const int PlayerNameMaxLength = 16;

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "CLOCK$", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>Extra invalid chars for Minecraft version folder names beyond <see cref="Path.GetInvalidFileNameChars"/>.</summary>
    private static readonly string[] MinecraftExtraInvalid = ["!", ";"];

    /// <summary>
    /// Validate a versions/ folder name. Returns localized error, or null if OK.
    /// </summary>
    public static string? ValidateVersionName(string? name, string? versionsParent = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Loc.Get(LocKeys.Validate_VersionEmpty);

        if (name.StartsWith(' '))
            return Loc.Get(LocKeys.Validate_NameLeadingSpace);

        if (name.EndsWith(' '))
            return Loc.Get(LocKeys.Validate_NameTrailingSpace);

        if (name.Length > VersionNameMaxLength)
            return Loc.Format(LocKeys.Validate_NameTooLong, VersionNameMaxLength);

        if (name.EndsWith('.'))
            return Loc.Get(LocKeys.Validate_NameTrailingDot);

        var invalid = FindInvalidChars(name, includeMinecraftExtras: true);
        if (invalid is not null)
            return Loc.Format(LocKeys.Validate_NameInvalidChar, invalid);

        var reserved = FindReservedWord(name);
        if (reserved is not null)
            return Loc.Format(LocKeys.Validate_NameReserved, reserved);

        // Reject NTFS 8.3 short names like "ABCDE~1".
        if (Ntfs83Pattern().IsMatch(name))
            return Loc.Get(LocKeys.Validate_NameNtfs83);

        if (!string.IsNullOrWhiteSpace(versionsParent) && Directory.Exists(versionsParent))
        {
            var exists = Directory.EnumerateDirectories(versionsParent)
                .Select(Path.GetFileName)
                .Any(existing =>
                    !string.IsNullOrEmpty(existing) &&
                    existing.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (exists)
                return Loc.Get(LocKeys.Validate_VersionExists);
        }

        return null;
    }

    /// <summary>
    /// Offline player name: non-empty, no ASCII quote, trimmed length at most 16.
    /// </summary>
    public static string? ValidatePlayerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Loc.Get(LocKeys.Validate_PlayerEmpty);

        var trimmed = name.Trim();
        if (trimmed.Contains('"'))
            return Loc.Get(LocKeys.Validate_PlayerQuote);

        if (trimmed.Length > PlayerNameMaxLength)
            return Loc.Get(LocKeys.Validate_PlayerTooLong);

        return null;
    }

    private static string? FindInvalidChars(string input, bool includeMinecraftExtras)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (input.Contains(c))
                found.Add(c.ToString());
        }

        if (includeMinecraftExtras)
        {
            foreach (var s in MinecraftExtraInvalid)
            {
                if (input.Contains(s, StringComparison.Ordinal))
                    found.Add(s);
            }
        }

        return found.Count == 0 ? null : string.Join(' ', found);
    }

    private static string? FindReservedWord(string input) =>
        ReservedNames.FirstOrDefault(r => r.Equals(input, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@".{2,}~\d", RegexOptions.CultureInvariant)]
    private static partial Regex Ntfs83Pattern();
}

