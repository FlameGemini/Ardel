using System.Security.Cryptography;
using System.Text;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Offline-mode player UUID from the public Minecraft protocol:
/// MD5("OfflinePlayer:" + name) as a version-3 UUID string.
/// Name matching is case-sensitive.
/// </summary>
public static class OfflinePlayerUuid
{
    public static string FromPlayerName(string playerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);

        var payload = Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName);
        var digest = MD5.HashData(payload);

        // RFC 4122 version 3 + IETF variant (same rules as Java UUID.nameUUIDFromBytes).
        digest[6] = (byte)((digest[6] & 0x0F) | 0x30);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);

        return FormatJavaStyle(digest);
    }

    private static string FormatJavaStyle(ReadOnlySpan<byte> bytes)
    {
        // Big-endian hex groups 8-4-4-4-12 (matches Java UUID.toString).
        Span<char> chars = stackalloc char[36];
        var i = 0;
        WriteHex(bytes, 0, 4, chars, ref i);
        chars[i++] = '-';
        WriteHex(bytes, 4, 2, chars, ref i);
        chars[i++] = '-';
        WriteHex(bytes, 6, 2, chars, ref i);
        chars[i++] = '-';
        WriteHex(bytes, 8, 2, chars, ref i);
        chars[i++] = '-';
        WriteHex(bytes, 10, 6, chars, ref i);
        return new string(chars);
    }

    private static void WriteHex(ReadOnlySpan<byte> src, int srcOffset, int count, Span<char> dest, ref int destIndex)
    {
        for (var n = 0; n < count; n++)
        {
            var b = src[srcOffset + n];
            dest[destIndex++] = ToHex((b >> 4) & 0xF);
            dest[destIndex++] = ToHex(b & 0xF);
        }
    }

    private static char ToHex(int value) => (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
