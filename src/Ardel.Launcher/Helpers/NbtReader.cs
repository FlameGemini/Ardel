using System.Buffers.Binary;
using System.Text;

namespace Ardel.Launcher.Helpers;

internal enum NbtType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12
}

internal abstract class NbtValue
{
    public abstract NbtType Type { get; }
}

internal sealed class NbtByteValue(sbyte value) : NbtValue
{
    public sbyte Value { get; set; } = value;
    public override NbtType Type => NbtType.Byte;
}

internal sealed class NbtShortValue(short value) : NbtValue
{
    public short Value { get; set; } = value;
    public override NbtType Type => NbtType.Short;
}

internal sealed class NbtIntValue(int value) : NbtValue
{
    public int Value { get; set; } = value;
    public override NbtType Type => NbtType.Int;
}

internal sealed class NbtLongValue(long value) : NbtValue
{
    public long Value { get; set; } = value;
    public override NbtType Type => NbtType.Long;
}

internal sealed class NbtFloatValue(float value) : NbtValue
{
    public float Value { get; set; } = value;
    public override NbtType Type => NbtType.Float;
}

internal sealed class NbtDoubleValue(double value) : NbtValue
{
    public double Value { get; set; } = value;
    public override NbtType Type => NbtType.Double;
}

internal sealed class NbtByteArrayValue(byte[] value) : NbtValue
{
    public byte[] Value { get; } = value;
    public override NbtType Type => NbtType.ByteArray;
}

internal sealed class NbtStringValue(string value) : NbtValue
{
    public string Value { get; set; } = value;
    public override NbtType Type => NbtType.String;
}

internal sealed class NbtListValue(NbtType elementType, List<NbtValue> items) : NbtValue
{
    public NbtType ElementType { get; } = elementType;
    public List<NbtValue> Items { get; } = items;
    public override NbtType Type => NbtType.List;
}

internal sealed class NbtCompoundValue : NbtValue
{
    public Dictionary<string, NbtValue> Children { get; } = new(StringComparer.Ordinal);
    public override NbtType Type => NbtType.Compound;
}

internal sealed class NbtIntArrayValue(int[] value) : NbtValue
{
    public int[] Value { get; } = value;
    public override NbtType Type => NbtType.IntArray;
}

internal sealed class NbtLongArrayValue(long[] value) : NbtValue
{
    public long[] Value { get; } = value;
    public override NbtType Type => NbtType.LongArray;
}

internal sealed class NbtRoot(string name, NbtCompoundValue compound)
{
    public string Name { get; } = name;
    public NbtCompoundValue Compound { get; } = compound;
}

/// <summary>Minimal NBT reader for Minecraft <c>level.dat</c> (big-endian, Java Edition).</summary>
internal static class NbtReader
{
    public static NbtRoot ReadRoot(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var tagType = (NbtType)reader.ReadByte();
        if (tagType != NbtType.Compound)
            throw new InvalidDataException($"Expected compound root, got tag {(byte)tagType}.");

        var name = ReadString(reader);
        return new NbtRoot(name, ReadCompoundPayload(reader));
    }

    private static NbtCompoundValue ReadCompoundPayload(BinaryReader reader)
    {
        var compound = new NbtCompoundValue();
        while (true)
        {
            var tagType = (NbtType)reader.ReadByte();
            if (tagType == NbtType.End)
                break;

            var name = ReadString(reader);
            compound.Children[name] = ReadPayload(reader, tagType);
        }

        return compound;
    }

    private static NbtValue ReadPayload(BinaryReader reader, NbtType tagType) => tagType switch
    {
        NbtType.Byte => new NbtByteValue(reader.ReadSByte()),
        NbtType.Short => new NbtShortValue(ReadInt16(reader)),
        NbtType.Int => new NbtIntValue(ReadInt32(reader)),
        NbtType.Long => new NbtLongValue(ReadInt64(reader)),
        NbtType.Float => new NbtFloatValue(ReadSingle(reader)),
        NbtType.Double => new NbtDoubleValue(ReadDouble(reader)),
        NbtType.ByteArray => new NbtByteArrayValue(reader.ReadBytes(ReadInt32(reader))),
        NbtType.String => new NbtStringValue(ReadString(reader)),
        NbtType.List => ReadList(reader),
        NbtType.Compound => ReadCompoundPayload(reader),
        NbtType.IntArray => new NbtIntArrayValue(ReadIntArray(reader)),
        NbtType.LongArray => new NbtLongArrayValue(ReadLongArray(reader)),
        _ => throw new InvalidDataException($"Unsupported NBT tag type {(byte)tagType}.")
    };

    private static NbtListValue ReadList(BinaryReader reader)
    {
        var elementType = (NbtType)reader.ReadByte();
        var length = ReadInt32(reader);
        var list = new List<NbtValue>(length);
        for (var i = 0; i < length; i++)
            list.Add(ReadPayload(reader, elementType));
        return new NbtListValue(elementType, list);
    }

    private static int[] ReadIntArray(BinaryReader reader)
    {
        var length = ReadInt32(reader);
        var array = new int[length];
        for (var i = 0; i < length; i++)
            array[i] = ReadInt32(reader);
        return array;
    }

    private static long[] ReadLongArray(BinaryReader reader)
    {
        var length = ReadInt32(reader);
        var array = new long[length];
        for (var i = 0; i < length; i++)
            array[i] = ReadInt64(reader);
        return array;
    }

    private static short ReadInt16(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        if (reader.Read(bytes) != 2)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadInt16BigEndian(bytes);
    }

    private static int ReadInt32(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != 4)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    private static long ReadInt64(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (reader.Read(bytes) != 8)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    private static float ReadSingle(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != 4)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadSingleBigEndian(bytes);
    }

    private static double ReadDouble(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (reader.Read(bytes) != 8)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadDoubleBigEndian(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = ReadInt16(reader);
        if (length <= 0)
            return string.Empty;

        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    public static bool TryGetCompound(NbtCompoundValue map, string key, out NbtCompoundValue compound)
    {
        if (map.Children.TryGetValue(key, out var value) && value is NbtCompoundValue nested)
        {
            compound = nested;
            return true;
        }

        compound = null!;
        return false;
    }

    public static bool TryGetString(NbtCompoundValue map, string key, out string? value)
    {
        if (map.Children.TryGetValue(key, out var raw) && raw is NbtStringValue s)
        {
            value = s.Value;
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryGetInt(NbtCompoundValue map, string key, out int value)
    {
        value = 0;
        if (!map.Children.TryGetValue(key, out var raw))
            return false;

        switch (raw)
        {
            case NbtIntValue i:
                value = i.Value;
                return true;
            case NbtByteValue b:
                value = b.Value;
                return true;
            case NbtShortValue s:
                value = s.Value;
                return true;
            case NbtLongValue l:
                value = (int)l.Value;
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetIntArray(NbtCompoundValue map, string key, out int[] value)
    {
        if (map.Children.TryGetValue(key, out var raw) && raw is NbtIntArrayValue array)
        {
            value = array.Value;
            return true;
        }

        value = [];
        return false;
    }

    public static bool TryGetLong(NbtCompoundValue map, string key, out long value)
    {
        value = 0;
        if (!map.Children.TryGetValue(key, out var raw))
            return false;

        switch (raw)
        {
            case NbtLongValue l:
                value = l.Value;
                return true;
            case NbtIntValue i:
                value = i.Value;
                return true;
            case NbtByteValue b:
                value = b.Value;
                return true;
            case NbtShortValue s:
                value = s.Value;
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetBool(NbtCompoundValue map, string key, out bool value)
    {
        if (!TryGetInt(map, key, out var number))
        {
            value = false;
            return false;
        }

        value = number != 0;
        return true;
    }
}
