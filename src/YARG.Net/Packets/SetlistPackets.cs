using System;
using System.Collections.Generic;
using System.Text;

namespace YARG.Net.Packets;

/// <summary>
/// Sent when a player adds a song to the setlist.
/// </summary>
public sealed record SetlistAddPacket(
    Guid SessionId,
    string SongHash,
    string PlayerName,
    string SongName,
    string SongArtist) : IPacketPayload;

/// <summary>
/// Sent when a player removes a song from the setlist.
/// </summary>
public sealed record SetlistRemovePacket(
    Guid SessionId,
    string SongHash,
    string PlayerName,
    string SongName,
    string SongArtist) : IPacketPayload;

/// <summary>
/// Sent to sync the full setlist to a newly joined client.
/// </summary>
public sealed record SetlistSyncPacket(
    Guid LobbyId,
    IReadOnlyList<SetlistEntry> Songs) : IPacketPayload;

/// <summary>
/// Sent when the host starts the show with the current setlist.
/// </summary>
public sealed record SetlistStartPacket(
    Guid LobbyId,
    IReadOnlyList<string> SongHashes) : IPacketPayload;

/// <summary>
/// Represents a single song entry in a setlist.
/// </summary>
public sealed record SetlistEntry(
    string SongHash,
    string SongName,
    string SongArtist,
    string AddedByPlayerName);

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for setlist-related messages.
/// </summary>
public static class SetlistBinaryPackets
{
    /// <summary>
    /// Builds a setlist add packet.
    /// </summary>
    public static byte[] BuildAddPacket(string songHash, string playerName, string songName, string artistName)
    {
        int size = 1 + // PacketType
                   PacketWriter.GetStringSize(songHash) +
                   PacketWriter.GetStringSize(playerName) +
                   PacketWriter.GetStringSize(songName) +
                   PacketWriter.GetStringSize(artistName);
        
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.SetlistAdd);
        writer.WriteString(songHash);
        writer.WriteString(playerName);
        writer.WriteString(songName);
        writer.WriteString(artistName);
        
        return buffer;
    }

    /// <summary>
    /// Builds a setlist remove packet.
    /// </summary>
    public static byte[] BuildRemovePacket(string songHash, string playerName, string songName, string artistName)
    {
        int size = 1 + // PacketType
                   PacketWriter.GetStringSize(songHash) +
                   PacketWriter.GetStringSize(playerName) +
                   PacketWriter.GetStringSize(songName) +
                   PacketWriter.GetStringSize(artistName);
        
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.SetlistRemove);
        writer.WriteString(songHash);
        writer.WriteString(playerName);
        writer.WriteString(songName);
        writer.WriteString(artistName);
        
        return buffer;
    }

    /// <summary>
    /// Builds a setlist sync packet with all entries.
    /// </summary>
    public static byte[] BuildSyncPacket(IReadOnlyList<SetlistEntry> entries)
    {
        // Calculate total size
        int size = 1 + 2; // PacketType + count
        foreach (var entry in entries)
        {
            size += GetEntrySize(entry);
        }
        
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.SetlistSync);
        writer.WriteUInt16((ushort)entries.Count);
        
        foreach (var entry in entries)
        {
            WriteEntry(ref writer, entry);
        }
        
        return buffer;
    }

    /// <summary>
    /// Builds a setlist start packet.
    /// </summary>
    public static byte[] BuildStartPacket(IReadOnlyList<string> songHashes)
    {
        // Calculate total size
        int size = 1 + 2; // PacketType + count
        foreach (var hash in songHashes)
        {
            size += PacketWriter.GetStringSize(hash);
        }
        
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.SetlistStart);
        writer.WriteUInt16((ushort)songHashes.Count);
        
        foreach (var hash in songHashes)
        {
            writer.WriteString(hash);
        }
        
        return buffer;
    }

    /// <summary>
    /// Parsed setlist entry data.
    /// </summary>
    public readonly struct ParsedEntry
    {
        public string SongHash { get; init; }
        public string PlayerName { get; init; }
        public string SongName { get; init; }
        public string ArtistName { get; init; }
    }

    /// <summary>
    /// Parses a setlist add or remove packet.
    /// </summary>
    public static bool TryParseAddOrRemove(ReadOnlySpan<byte> data, out ParsedEntry entry)
    {
        entry = default;
        
        if (data.Length < 9) // Type + 4 empty strings
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            entry = new ParsedEntry
            {
                SongHash = reader.ReadString(),
                PlayerName = reader.ReadString(),
                SongName = reader.ReadString(),
                ArtistName = reader.ReadString()
            };
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a setlist sync packet.
    /// </summary>
    public static bool TryParseSyncPacket(ReadOnlySpan<byte> data, out List<ParsedEntry> entries)
    {
        entries = new List<ParsedEntry>();
        
        if (data.Length < 3) // Type + count
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            int count = reader.ReadUInt16();
            
            for (int i = 0; i < count; i++)
            {
                entries.Add(new ParsedEntry
                {
                    SongHash = reader.ReadString(),
                    PlayerName = reader.ReadString(),
                    SongName = reader.ReadString(),
                    ArtistName = reader.ReadString()
                });
            }
            
            return true;
        }
        catch
        {
            entries.Clear();
            return false;
        }
    }

    /// <summary>
    /// Parses a setlist start packet.
    /// </summary>
    public static bool TryParseStartPacket(ReadOnlySpan<byte> data, out List<string> songHashes)
    {
        songHashes = new List<string>();
        
        if (data.Length < 3) // Type + count
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            int count = reader.ReadUInt16();
            
            for (int i = 0; i < count; i++)
            {
                songHashes.Add(reader.ReadString());
            }
            
            return true;
        }
        catch
        {
            songHashes.Clear();
            return false;
        }
    }

    private static int GetEntrySize(SetlistEntry entry)
    {
        return PacketWriter.GetStringSize(entry.SongHash) +
               PacketWriter.GetStringSize(entry.AddedByPlayerName) +
               PacketWriter.GetStringSize(entry.SongName) +
               PacketWriter.GetStringSize(entry.SongArtist);
    }

    private static void WriteEntry(ref PacketWriter writer, SetlistEntry entry)
    {
        writer.WriteString(entry.SongHash);
        writer.WriteString(entry.AddedByPlayerName);
        writer.WriteString(entry.SongName);
        writer.WriteString(entry.SongArtist);
    }
}

#endregion
