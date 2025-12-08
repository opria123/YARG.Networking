using System;
using System.Runtime.CompilerServices;

namespace YARG.Net.Utilities;

/// <summary>
/// Helper methods for reading and writing primitive types in network byte order (big-endian).
/// </summary>
public static class BinaryUtils
{
    #region Write Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt16(byte[] buffer, ref int offset, short value)
    {
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
    {
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(byte[] buffer, ref int offset, int value)
    {
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(byte[] buffer, ref int offset, uint value)
    {
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(byte[] buffer, ref int offset, long value)
    {
        buffer[offset++] = (byte)(value >> 56);
        buffer[offset++] = (byte)(value >> 48);
        buffer[offset++] = (byte)(value >> 40);
        buffer[offset++] = (byte)(value >> 32);
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    public static void WriteFloat(byte[] buffer, ref int offset, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        Array.Copy(bytes, 0, buffer, offset, 4);
        offset += 4;
    }

    public static void WriteDouble(byte[] buffer, ref int offset, double value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        Array.Copy(bytes, 0, buffer, offset, 8);
        offset += 8;
    }

    public static void WriteString(byte[] buffer, ref int offset, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt16(buffer, ref offset, (ushort)bytes.Length);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        offset += bytes.Length;
    }

    public static void WriteBool(byte[] buffer, ref int offset, bool value)
    {
        buffer[offset++] = value ? (byte)1 : (byte)0;
    }

    #endregion

    #region Read Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ReadInt16(ReadOnlySpan<byte> span, ref int offset)
    {
        short value = (short)((span[offset] << 8) | span[offset + 1]);
        offset += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16(ReadOnlySpan<byte> span, ref int offset)
    {
        ushort value = (ushort)((span[offset] << 8) | span[offset + 1]);
        offset += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32(ReadOnlySpan<byte> span, ref int offset)
    {
        int value = (span[offset] << 24) | (span[offset + 1] << 16) | (span[offset + 2] << 8) | span[offset + 3];
        offset += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32(ReadOnlySpan<byte> span, ref int offset)
    {
        uint value = ((uint)span[offset] << 24) | ((uint)span[offset + 1] << 16) | ((uint)span[offset + 2] << 8) | span[offset + 3];
        offset += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64(ReadOnlySpan<byte> span, ref int offset)
    {
        long value = ((long)span[offset] << 56) | ((long)span[offset + 1] << 48) |
                     ((long)span[offset + 2] << 40) | ((long)span[offset + 3] << 32) |
                     ((long)span[offset + 4] << 24) | ((long)span[offset + 5] << 16) |
                     ((long)span[offset + 6] << 8) | span[offset + 7];
        offset += 8;
        return value;
    }

    public static float ReadFloat(ReadOnlySpan<byte> span, ref int offset)
    {
        byte[] bytes = new byte[4];
        for (int i = 0; i < 4; i++)
            bytes[i] = span[offset + i];
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        offset += 4;
        return BitConverter.ToSingle(bytes, 0);
    }

    public static double ReadDouble(ReadOnlySpan<byte> span, ref int offset)
    {
        byte[] bytes = new byte[8];
        for (int i = 0; i < 8; i++)
            bytes[i] = span[offset + i];
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        offset += 8;
        return BitConverter.ToDouble(bytes, 0);
    }

    public static string ReadString(ReadOnlySpan<byte> span, ref int offset)
    {
        ushort length = ReadUInt16(span, ref offset);
        if (length == 0)
            return string.Empty;

        string value = System.Text.Encoding.UTF8.GetString(span.Slice(offset, length));
        offset += length;
        return value;
    }

    public static bool ReadBool(ReadOnlySpan<byte> span, ref int offset)
    {
        return span[offset++] != 0;
    }

    #endregion

    #region Span Write Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(Span<byte> span, int offset, int value)
    {
        span[offset] = (byte)(value >> 24);
        span[offset + 1] = (byte)(value >> 16);
        span[offset + 2] = (byte)(value >> 8);
        span[offset + 3] = (byte)value;
    }

    #endregion
}
