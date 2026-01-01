using System;
using System.Collections.Generic;

namespace YARG.Net.Packets;

public sealed record HandshakeRequestPacket(string ClientVersion, string PlayerName, string? Password = null) : IPacketPayload;

public sealed record HandshakeResponsePacket(bool Accepted, string? Reason, Guid SessionId) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for handshake-related messages.
/// </summary>
public static class HandshakeBinaryPackets
{
    /// <summary>
    /// Builds a handshake request packet with player identity (GUID + display name).
    /// </summary>
    /// <param name="playerId">The player's unique persistent ID.</param>
    /// <param name="displayName">The player's display name.</param>
    public static byte[] BuildRequestPacket(Guid playerId, string displayName)
    {
        int size = 1 + 16 + PacketWriter.GetStringSize(displayName); // Type + GUID + string
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.HandshakeRequest);
        writer.WriteGuid(playerId);
        writer.WriteString(displayName);
        
        return buffer;
    }

    /// <summary>
    /// Builds a handshake request packet with player identity.
    /// </summary>
    /// <param name="identity">The player's network identity.</param>
    public static byte[] BuildRequestPacket(NetworkPlayerIdentity identity)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        return BuildRequestPacket(identity.PlayerId, identity.DisplayName);
    }

    /// <summary>
    /// Parses a handshake request packet into a player identity.
    /// </summary>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out NetworkPlayerIdentity? identity)
    {
        identity = null;
        
        if (data.Length < 19) // Type + GUID(16) + min string length(2)
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            var playerId = reader.ReadGuid();
            var displayName = reader.ReadString();
            identity = NetworkPlayerIdentity.FromData(playerId, displayName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a handshake response packet.
    /// </summary>
    /// <param name="accepted">Whether the handshake was accepted.</param>
    /// <param name="sessionId">The session ID if accepted.</param>
    /// <param name="reason">The rejection reason if not accepted.</param>
    public static byte[] BuildResponsePacket(bool accepted, Guid sessionId, string? reason = null)
    {
        reason ??= string.Empty;
        int size = 1 + 1 + 16 + PacketWriter.GetStringSize(reason); // Type + bool + GUID + string
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.HandshakeResponse);
        writer.WriteBool(accepted);
        writer.WriteGuid(sessionId);
        writer.WriteString(reason);
        
        return buffer;
    }

    /// <summary>
    /// Parses a handshake response packet.
    /// </summary>
    public static bool TryParseResponsePacket(ReadOnlySpan<byte> data, out bool accepted, out Guid sessionId, out string? reason)
    {
        accepted = false;
        sessionId = Guid.Empty;
        reason = null;
        
        if (data.Length < 18) // Type + bool + GUID
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            accepted = reader.ReadBool();
            sessionId = reader.ReadGuid();
            reason = reader.ReadString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a handshake request packet with multiple player identities.
    /// Used when a client has multiple local profiles to register with the lobby.
    /// </summary>
    /// <param name="identities">The list of player identities to send.</param>
    public static byte[] BuildMultiPlayerRequestPacket(IReadOnlyList<NetworkPlayerIdentity> identities)
    {
        if (identities is null || identities.Count == 0)
        {
            throw new ArgumentException("At least one player identity is required.", nameof(identities));
        }

        // Calculate total size: Type(1) + Count(1) + [GUID(16) + String per identity]
        int size = 1 + 1; // PacketType + player count byte
        foreach (var identity in identities)
        {
            size += 16 + PacketWriter.GetStringSize(identity.DisplayName);
        }

        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.HandshakeRequest);
        writer.WriteByte((byte)identities.Count);
        
        foreach (var identity in identities)
        {
            writer.WriteGuid(identity.PlayerId);
            writer.WriteString(identity.DisplayName);
        }
        
        return buffer;
    }

    /// <summary>
    /// Parses a handshake request packet that may contain multiple player identities.
    /// Returns the list of identities if successful.
    /// </summary>
    public static bool TryParseMultiPlayerRequestPacket(ReadOnlySpan<byte> data, out List<NetworkPlayerIdentity>? identities)
    {
        identities = null;
        
        if (data.Length < 2) // Type + count minimum
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            int count = reader.ReadByte();
            if (count == 0)
                return false;
            
            identities = new List<NetworkPlayerIdentity>(count);
            
            for (int i = 0; i < count; i++)
            {
                var playerId = reader.ReadGuid();
                var displayName = reader.ReadString();
                identities.Add(NetworkPlayerIdentity.FromData(playerId, displayName));
            }
            
            return true;
        }
        catch
        {
            identities = null;
            return false;
        }
    }
}

#endregion
