using System.Buffers.Binary;
using System.Text;

namespace Ardel.Launcher.Helpers;

/// <summary>Writes typed NBT produced by <see cref="NbtReader"/> back to a stream (big-endian).</summary>
internal static class NbtWriter
{
    public static void WriteRoot(Stream stream, NbtRoot root)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)NbtType.Compound);
        WriteString(writer, root.Name);
        WriteCompoundPayload(writer, root.Compound);
    }

    private static void WriteCompoundPayload(BinaryWriter writer, NbtCompoundValue compound)
    {
        foreach (var (name, value) in compound.Children)
        {
            writer.Write((byte)value.Type);
            WriteString(writer, name);
            WritePayload(writer, value);
        }

        writer.Write((byte)NbtType.End);
    }

    private static void WritePayload(BinaryWriter writer, NbtValue value)
    {
        switch (value)
        {
            case NbtByteValue b:
                writer.Write(unchecked((byte)b.Value));
                break;
            case NbtShortValue s:
                WriteInt16(writer, s.Value);
                break;
            case NbtIntValue i:
                WriteInt32(writer, i.Value);
                break;
            case NbtLongValue l:
                WriteInt64(writer, l.Value);
                break;
            case NbtFloatValue f:
                WriteSingle(writer, f.Value);
                break;
            case NbtDoubleValue d:
                WriteDouble(writer, d.Value);
                break;
            case NbtByteArrayValue bytes:
                WriteInt32(writer, bytes.Value.Length);
                writer.Write(bytes.Value);
                break;
            case NbtStringValue s:
                WriteString(writer, s.Value);
                break;
            case NbtListValue list:
                writer.Write((byte)list.ElementType);
                WriteInt32(writer, list.Items.Count);
                foreach (var item in list.Items)
                    WritePayload(writer, item);
                break;
            case NbtCompoundValue compound:
                WriteCompoundPayload(writer, compound);
                break;
            case NbtIntArrayValue ints:
                WriteInt32(writer, ints.Value.Length);
                foreach (var item in ints.Value)
                    WriteInt32(writer, item);
                break;
            case NbtLongArrayValue longs:
                WriteInt32(writer, longs.Value.Length);
                foreach (var item in longs.Value)
                    WriteInt64(writer, item);
                break;
            default:
                throw new InvalidDataException($"Unsupported NBT value {value.GetType().Name}.");
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > short.MaxValue)
            throw new InvalidDataException("NBT string is too long.");

        WriteInt16(writer, (short)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteInt16(BinaryWriter writer, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInt32(BinaryWriter writer, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInt64(BinaryWriter writer, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteSingle(BinaryWriter writer, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteDouble(BinaryWriter writer, double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        writer.Write(bytes);
    }
}
