using System;
using System.Collections.Generic;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by a client to authenticate with a password-protected lobby.
/// </summary>
public sealed record AuthRequestPacket(
    Guid SessionId,
    string Password) : IPacketPayload;

/// <summary>
/// Sent by the server in response to an authentication request.
/// </summary>
public sealed record AuthResponsePacket(
    Guid SessionId,
    AuthResult Result,
    string? ErrorMessage = null) : IPacketPayload;

/// <summary>
/// Authentication result codes.
/// </summary>
public enum AuthResult
{
    /// <summary>
    /// Authentication succeeded.
    /// </summary>
    Success = 0,
    
    /// <summary>
    /// The password was incorrect.
    /// </summary>
    WrongPassword = 1,
    
    /// <summary>
    /// The lobby is full.
    /// </summary>
    LobbyFull = 2,
    
    /// <summary>
    /// The lobby no longer exists.
    /// </summary>
    LobbyNotFound = 3,
    
    /// <summary>
    /// The player is banned from this lobby.
    /// </summary>
    Banned = 4,
}

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for authentication-related messages.
/// </summary>
public static class AuthBinaryPackets
{
    /// <summary>
    /// Builds an auth request packet with password, profile count, and optional player identities.
    /// </summary>
    /// <param name="password">The lobby password.</param>
    /// <param name="profileCount">The number of local profiles the client wants to join with.</param>
    /// <param name="playerIdentities">Optional list of player identities to include (for combined auth+handshake).</param>
    public static byte[] BuildRequestPacket(string password, int profileCount = 1, List<NetworkPlayerIdentity>? playerIdentities = null)
    {
        // Calculate size
        int size = 1 + PacketWriter.GetStringSize(password) + 1; // Type + password + profileCount byte
        
        // Add size for player identities if provided
        bool hasIdentities = playerIdentities != null && playerIdentities.Count > 0;
        size += 1; // hasIdentities flag
        
        if (hasIdentities)
        {
            size += 1; // identity count
            foreach (var identity in playerIdentities!)
            {
                size += 16; // PlayerId GUID
                size += PacketWriter.GetStringSize(identity.DisplayName);
            }
        }
        
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.AuthRequest);
        writer.WriteString(password);
        writer.WriteByte((byte)Math.Clamp(profileCount, 1, 64));
        
        // Write player identities flag and data
        writer.WriteBool(hasIdentities);
        if (hasIdentities)
        {
            writer.WriteByte((byte)playerIdentities!.Count);
            foreach (var identity in playerIdentities)
            {
                writer.WriteGuid(identity.PlayerId);
                writer.WriteString(identity.DisplayName);
            }
        }
        
        return buffer;
    }

    /// <summary>
    /// Builds an auth response packet.
    /// </summary>
    public static byte[] BuildResponsePacket(bool success, string? message = null)
    {
        message ??= string.Empty;
        int size = 1 + 1 + PacketWriter.GetStringSize(message); // Type + bool + string
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.AuthResponse);
        writer.WriteBool(success);
        writer.WriteString(message);
        
        return buffer;
    }

    /// <summary>
    /// Parses an auth request packet with optional player identities.
    /// </summary>
    /// <param name="data">The packet data.</param>
    /// <param name="password">The parsed password.</param>
    /// <param name="profileCount">The number of profiles the client wants to join with.</param>
    /// <param name="playerIdentities">The player identities if included.</param>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password, out int profileCount, out List<NetworkPlayerIdentity>? playerIdentities)
    {
        password = string.Empty;
        profileCount = 1;
        playerIdentities = null;
        
        if (data.Length < 3) // Type + min string length
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            password = reader.ReadString();
            
            // Read profile count if available (backwards compatible with old clients)
            if (reader.Remaining >= 1)
            {
                profileCount = reader.ReadByte();
                if (profileCount < 1) profileCount = 1;
            }
            
            // Read player identities if available (new combined auth+handshake)
            if (reader.Remaining >= 1)
            {
                bool hasIdentities = reader.ReadBool();
                if (hasIdentities && reader.Remaining >= 1)
                {
                    int identityCount = reader.ReadByte();
                    playerIdentities = new List<NetworkPlayerIdentity>(identityCount);
                    
                    for (int i = 0; i < identityCount && reader.Remaining >= 18; i++) // 16 (GUID) + 2 (min string) minimum
                    {
                        var playerId = reader.ReadGuid();
                        var displayName = reader.ReadString();
                        playerIdentities.Add(new NetworkPlayerIdentity(playerId, displayName));
                    }
                }
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses an auth request packet (backwards compatible overload).
    /// </summary>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password, out int profileCount)
    {
        return TryParseRequestPacket(data, out password, out profileCount, out _);
    }

    /// <summary>
    /// Parses an auth request packet (backwards compatible overload).
    /// </summary>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password)
    {
        return TryParseRequestPacket(data, out password, out _, out _);
    }

    /// <summary>
    /// Parses an auth response packet.
    /// </summary>
    public static bool TryParseResponsePacket(ReadOnlySpan<byte> data, out bool success, out string message)
    {
        success = false;
        message = string.Empty;
        
        if (data.Length < 4) // Type + bool + min string length(2)
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            success = reader.ReadBool();
            message = reader.ReadString();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Alias for <see cref="AuthBinaryPackets"/> for compatibility.
/// </summary>
public static class AuthenticationBinaryPackets
{
    /// <inheritdoc cref="AuthBinaryPackets.BuildRequestPacket"/>
    public static byte[] BuildRequestPacket(string password, int profileCount = 1, List<NetworkPlayerIdentity>? playerIdentities = null) 
        => AuthBinaryPackets.BuildRequestPacket(password, profileCount, playerIdentities);

    /// <inheritdoc cref="AuthBinaryPackets.BuildResponsePacket"/>
    public static byte[] BuildResponsePacket(bool success, string? message = null) => AuthBinaryPackets.BuildResponsePacket(success, message);

    /// <inheritdoc cref="AuthBinaryPackets.TryParseRequestPacket(ReadOnlySpan{byte}, out string, out int, out List{NetworkPlayerIdentity})"/>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password, out int profileCount, out List<NetworkPlayerIdentity>? playerIdentities) 
        => AuthBinaryPackets.TryParseRequestPacket(data, out password, out profileCount, out playerIdentities);

    /// <inheritdoc cref="AuthBinaryPackets.TryParseRequestPacket(ReadOnlySpan{byte}, out string, out int)"/>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password, out int profileCount) => AuthBinaryPackets.TryParseRequestPacket(data, out password, out profileCount);

    /// <inheritdoc cref="AuthBinaryPackets.TryParseRequestPacket(ReadOnlySpan{byte}, out string)"/>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password) => AuthBinaryPackets.TryParseRequestPacket(data, out password);

    /// <inheritdoc cref="AuthBinaryPackets.TryParseResponsePacket"/>
    public static bool TryParseResponsePacket(ReadOnlySpan<byte> data, out bool success, out string message) => AuthBinaryPackets.TryParseResponsePacket(data, out success, out message);
}

#endregion
