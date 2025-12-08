using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace YARG.Net.Packets;

/// <summary>
/// Efficient packet writer for building network messages.
/// Handles length-prefixed strings and binary serialization in network byte order.
/// </summary>
public ref struct PacketWriter
{
    private readonly Span<byte> _buffer;
    private int _offset;

    public PacketWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
    }

    public PacketWriter(byte[] buffer) : this(buffer.AsSpan())
    {
    }

    /// <summary>
    /// Current write position in the buffer.
    /// </summary>
    public int Position => _offset;

    /// <summary>
    /// Remaining capacity in the buffer.
    /// </summary>
    public int Remaining => _buffer.Length - _offset;

    /// <summary>
    /// Gets the written portion of the buffer.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer[.._offset];

    /// <summary>
    /// Writes a packet type byte.
    /// </summary>
    public void WritePacketType(PacketType type)
    {
        _buffer[_offset++] = (byte)type;
    }

    /// <summary>
    /// Writes a single byte.
    /// </summary>
    public void WriteByte(byte value)
    {
        _buffer[_offset++] = value;
    }

    /// <summary>
    /// Writes a boolean as a single byte.
    /// </summary>
    public void WriteBool(bool value)
    {
        _buffer[_offset++] = value ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Writes a 16-bit integer in big-endian format.
    /// </summary>
    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(_buffer.Slice(_offset, 2), value);
        _offset += 2;
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer in big-endian format.
    /// </summary>
    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.Slice(_offset, 2), value);
        _offset += 2;
    }

    /// <summary>
    /// Writes a 32-bit integer in big-endian format.
    /// </summary>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_buffer.Slice(_offset, 4), value);
        _offset += 4;
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer in big-endian format.
    /// </summary>
    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.Slice(_offset, 4), value);
        _offset += 4;
    }

    /// <summary>
    /// Writes a 64-bit integer in big-endian format.
    /// </summary>
    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(_buffer.Slice(_offset, 8), value);
        _offset += 8;
    }

    /// <summary>
    /// Writes a float in big-endian format.
    /// </summary>
    public void WriteFloat(float value)
    {
        int intValue = BitConverter.SingleToInt32Bits(value);
        BinaryPrimitives.WriteInt32BigEndian(_buffer.Slice(_offset, 4), intValue);
        _offset += 4;
    }

    /// <summary>
    /// Writes a double in big-endian format.
    /// </summary>
    public void WriteDouble(double value)
    {
        long longValue = BitConverter.DoubleToInt64Bits(value);
        BinaryPrimitives.WriteInt64BigEndian(_buffer.Slice(_offset, 8), longValue);
        _offset += 8;
    }

    /// <summary>
    /// Writes a GUID as 16 bytes.
    /// </summary>
    public void WriteGuid(Guid value)
    {
        value.TryWriteBytes(_buffer.Slice(_offset, 16));
        _offset += 16;
    }

    /// <summary>
    /// Writes a length-prefixed UTF-8 string (2-byte length prefix).
    /// </summary>
    public void WriteString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteUInt16(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > ushort.MaxValue)
        {
            throw new ArgumentException($"String too long: {byteCount} bytes exceeds maximum of {ushort.MaxValue}");
        }

        WriteUInt16((ushort)byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.Slice(_offset, byteCount));
        _offset += byteCount;
    }

    /// <summary>
    /// Writes a length-prefixed UTF-8 string with 1-byte length prefix.
    /// </summary>
    public void WriteShortString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteByte(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > byte.MaxValue)
        {
            throw new ArgumentException($"String too long: {byteCount} bytes exceeds maximum of {byte.MaxValue}");
        }

        WriteByte((byte)byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.Slice(_offset, byteCount));
        _offset += byteCount;
    }

    /// <summary>
    /// Writes raw bytes.
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buffer.Slice(_offset, data.Length));
        _offset += data.Length;
    }

    /// <summary>
    /// Writes a menu target enum value.
    /// </summary>
    public void WriteMenuTarget(MenuTarget target)
    {
        WriteByte((byte)target);
    }

    /// <summary>
    /// Calculates the byte size needed for a length-prefixed string.
    /// </summary>
    public static int GetStringSize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 2; // Just the length prefix
        return 2 + Encoding.UTF8.GetByteCount(value);
    }

    /// <summary>
    /// Calculates the byte size needed for a short length-prefixed string.
    /// </summary>
    public static int GetShortStringSize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 1; // Just the length prefix
        return 1 + Encoding.UTF8.GetByteCount(value);
    }
}

/// <summary>
/// Efficient packet reader for parsing network messages.
/// Handles length-prefixed strings and binary deserialization from network byte order.
/// </summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _offset;

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
    }

    public PacketReader(ReadOnlyMemory<byte> buffer) : this(buffer.Span)
    {
    }

    /// <summary>
    /// Current read position in the buffer.
    /// </summary>
    public int Position => _offset;

    /// <summary>
    /// Remaining bytes to read.
    /// </summary>
    public int Remaining => _buffer.Length - _offset;

    /// <summary>
    /// Total length of the buffer.
    /// </summary>
    public int Length => _buffer.Length;

    /// <summary>
    /// Whether all data has been read.
    /// </summary>
    public bool IsAtEnd => _offset >= _buffer.Length;

    /// <summary>
    /// Reads a packet type byte.
    /// </summary>
    public PacketType ReadPacketType()
    {
        return (PacketType)_buffer[_offset++];
    }

    /// <summary>
    /// Peeks at the packet type without advancing position.
    /// </summary>
    public PacketType PeekPacketType()
    {
        return (PacketType)_buffer[_offset];
    }

    /// <summary>
    /// Reads a single byte.
    /// </summary>
    public byte ReadByte()
    {
        return _buffer[_offset++];
    }

    /// <summary>
    /// Reads a boolean from a single byte.
    /// </summary>
    public bool ReadBool()
    {
        return _buffer[_offset++] != 0;
    }

    /// <summary>
    /// Reads a 16-bit integer in big-endian format.
    /// </summary>
    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(_buffer.Slice(_offset, 2));
        _offset += 2;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer in big-endian format.
    /// </summary>
    public ushort ReadUInt16()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_offset, 2));
        _offset += 2;
        return value;
    }

    /// <summary>
    /// Reads a 32-bit integer in big-endian format.
    /// </summary>
    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_offset, 4));
        _offset += 4;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer in big-endian format.
    /// </summary>
    public uint ReadUInt32()
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_offset, 4));
        _offset += 4;
        return value;
    }

    /// <summary>
    /// Reads a 64-bit integer in big-endian format.
    /// </summary>
    public long ReadInt64()
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(_buffer.Slice(_offset, 8));
        _offset += 8;
        return value;
    }

    /// <summary>
    /// Reads a float in big-endian format.
    /// </summary>
    public float ReadFloat()
    {
        int intValue = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_offset, 4));
        _offset += 4;
        return BitConverter.Int32BitsToSingle(intValue);
    }

    /// <summary>
    /// Reads a double in big-endian format.
    /// </summary>
    public double ReadDouble()
    {
        long longValue = BinaryPrimitives.ReadInt64BigEndian(_buffer.Slice(_offset, 8));
        _offset += 8;
        return BitConverter.Int64BitsToDouble(longValue);
    }

    /// <summary>
    /// Reads a GUID from 16 bytes.
    /// </summary>
    public Guid ReadGuid()
    {
        var guid = new Guid(_buffer.Slice(_offset, 16));
        _offset += 16;
        return guid;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string (2-byte length prefix).
    /// </summary>
    public string ReadString()
    {
        ushort length = ReadUInt16();
        if (length == 0)
            return string.Empty;

        string value = Encoding.UTF8.GetString(_buffer.Slice(_offset, length));
        _offset += length;
        return value;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string with 1-byte length prefix.
    /// </summary>
    public string ReadShortString()
    {
        byte length = ReadByte();
        if (length == 0)
            return string.Empty;

        string value = Encoding.UTF8.GetString(_buffer.Slice(_offset, length));
        _offset += length;
        return value;
    }

    /// <summary>
    /// Reads raw bytes into a new array.
    /// </summary>
    public byte[] ReadBytes(int count)
    {
        byte[] data = _buffer.Slice(_offset, count).ToArray();
        _offset += count;
        return data;
    }

    /// <summary>
    /// Skips a number of bytes.
    /// </summary>
    public void Skip(int count)
    {
        _offset += count;
    }

    /// <summary>
    /// Reads a menu target enum value.
    /// </summary>
    public MenuTarget ReadMenuTarget()
    {
        return (MenuTarget)ReadByte();
    }

    /// <summary>
    /// Checks if there are at least the specified number of bytes remaining.
    /// </summary>
    public bool HasBytes(int count)
    {
        return Remaining >= count;
    }
}
