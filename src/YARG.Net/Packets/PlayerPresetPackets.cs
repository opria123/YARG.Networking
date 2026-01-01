using System;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by clients to host with their visual preset data for syncing.
/// Also used by host to broadcast preset data to all clients.
/// </summary>
public sealed record PlayerPresetSyncPacket(
    Guid PlayerId,
    Guid CameraPresetId,
    string CameraPresetJson,
    Guid HighwayPresetId,
    string HighwayPresetJson,
    Guid ColorProfileId,
    string ColorProfileJson,
    Guid ThemePresetId,
    string ThemePresetJson) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for player preset sync messages.
/// Used when EnablePresetSync is enabled to sync visual presets between players.
/// </summary>
public static class PlayerPresetBinaryPackets
{
    /// <summary>
    /// Builds a player preset sync packet.
    /// Format: [PacketType (1)][PlayerId (16)][CameraId (16)][HighwayId (16)][ColorId (16)][ThemeId (16)]
    ///         [CameraJsonLen (4)][CameraJson][HighwayJsonLen (4)][HighwayJson][ColorJsonLen (4)][ColorJson][ThemeJsonLen (4)][ThemeJson]
    /// </summary>
    public static byte[] BuildPresetSyncPacket(
        Guid playerId,
        Guid cameraPresetId, string cameraPresetJson,
        Guid highwayPresetId, string highwayPresetJson,
        Guid colorProfileId, string colorProfileJson,
        Guid themePresetId, string themePresetJson)
    {
        cameraPresetJson ??= string.Empty;
        highwayPresetJson ??= string.Empty;
        colorProfileJson ??= string.Empty;
        themePresetJson ??= string.Empty;

        byte[] cameraJsonBytes = System.Text.Encoding.UTF8.GetBytes(cameraPresetJson);
        byte[] highwayJsonBytes = System.Text.Encoding.UTF8.GetBytes(highwayPresetJson);
        byte[] colorJsonBytes = System.Text.Encoding.UTF8.GetBytes(colorProfileJson);
        byte[] themeJsonBytes = System.Text.Encoding.UTF8.GetBytes(themePresetJson);

        // Calculate total size
        int size = 1 // PacketType
                 + 16 // PlayerId
                 + 16 // CameraPresetId
                 + 16 // HighwayPresetId
                 + 16 // ColorProfileId
                 + 16 // ThemePresetId
                 + 4 + cameraJsonBytes.Length  // CameraJson length + data
                 + 4 + highwayJsonBytes.Length // HighwayJson length + data
                 + 4 + colorJsonBytes.Length   // ColorJson length + data
                 + 4 + themeJsonBytes.Length;  // ThemeJson length + data

        byte[] buffer = new byte[size];
        int offset = 0;

        // Write packet type
        buffer[offset++] = (byte)PacketType.PlayerPresetSync;

        // Write PlayerId
        WriteGuid(buffer, ref offset, playerId);

        // Write preset IDs
        WriteGuid(buffer, ref offset, cameraPresetId);
        WriteGuid(buffer, ref offset, highwayPresetId);
        WriteGuid(buffer, ref offset, colorProfileId);
        WriteGuid(buffer, ref offset, themePresetId);

        // Write JSON data with lengths
        WriteByteArray(buffer, ref offset, cameraJsonBytes);
        WriteByteArray(buffer, ref offset, highwayJsonBytes);
        WriteByteArray(buffer, ref offset, colorJsonBytes);
        WriteByteArray(buffer, ref offset, themeJsonBytes);

        return buffer;
    }

    /// <summary>
    /// Parsed player preset sync data.
    /// </summary>
    public readonly struct ParsedPresetSync
    {
        public bool IsValid { get; init; }
        public Guid PlayerId { get; init; }
        public Guid CameraPresetId { get; init; }
        public string CameraPresetJson { get; init; }
        public Guid HighwayPresetId { get; init; }
        public string HighwayPresetJson { get; init; }
        public Guid ColorProfileId { get; init; }
        public string ColorProfileJson { get; init; }
        public Guid ThemePresetId { get; init; }
        public string ThemePresetJson { get; init; }
    }

    /// <summary>
    /// Parses a player preset sync packet.
    /// </summary>
    public static ParsedPresetSync ParsePresetSyncPacket(ReadOnlySpan<byte> data)
    {
        // Minimum size: 1 (type) + 16*5 (guids) + 4*4 (json lengths) = 97 bytes minimum
        if (data.Length < 97)
            return new ParsedPresetSync { IsValid = false };

        int offset = 1; // Skip packet type

        var playerId = ReadGuid(data, ref offset);
        var cameraPresetId = ReadGuid(data, ref offset);
        var highwayPresetId = ReadGuid(data, ref offset);
        var colorProfileId = ReadGuid(data, ref offset);
        var themePresetId = ReadGuid(data, ref offset);

        if (!TryReadString(data, ref offset, out string cameraJson))
            return new ParsedPresetSync { IsValid = false };

        if (!TryReadString(data, ref offset, out string highwayJson))
            return new ParsedPresetSync { IsValid = false };

        if (!TryReadString(data, ref offset, out string colorJson))
            return new ParsedPresetSync { IsValid = false };

        if (!TryReadString(data, ref offset, out string themeJson))
            return new ParsedPresetSync { IsValid = false };

        return new ParsedPresetSync
        {
            IsValid = true,
            PlayerId = playerId,
            CameraPresetId = cameraPresetId,
            CameraPresetJson = cameraJson,
            HighwayPresetId = highwayPresetId,
            HighwayPresetJson = highwayJson,
            ColorProfileId = colorProfileId,
            ColorProfileJson = colorJson,
            ThemePresetId = themePresetId,
            ThemePresetJson = themeJson
        };
    }

    #region Helper Methods

    private static void WriteGuid(byte[] buffer, ref int offset, Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        Array.Copy(bytes, 0, buffer, offset, 16);
        offset += 16;
    }

    private static void WriteByteArray(byte[] buffer, ref int offset, byte[] data)
    {
        // Write length as 4 bytes (big-endian)
        buffer[offset++] = (byte)(data.Length >> 24);
        buffer[offset++] = (byte)(data.Length >> 16);
        buffer[offset++] = (byte)(data.Length >> 8);
        buffer[offset++] = (byte)(data.Length & 0xFF);

        // Write data
        Array.Copy(data, 0, buffer, offset, data.Length);
        offset += data.Length;
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> data, ref int offset)
    {
        byte[] guidBytes = data.Slice(offset, 16).ToArray();
        offset += 16;
        return new Guid(guidBytes);
    }

    private static bool TryReadString(ReadOnlySpan<byte> data, ref int offset, out string result)
    {
        result = string.Empty;

        if (data.Length < offset + 4)
            return false;

        // Read length (big-endian)
        int length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        offset += 4;

        if (length < 0 || data.Length < offset + length)
            return false;

        if (length == 0)
        {
            result = string.Empty;
            return true;
        }

        result = System.Text.Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length;
        return true;
    }

    #endregion
}

#endregion
