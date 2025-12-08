using System;

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
    /// Builds an auth request packet with password.
    /// </summary>
    public static byte[] BuildRequestPacket(string password)
    {
        int size = 1 + PacketWriter.GetStringSize(password);
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.AuthRequest);
        writer.WriteString(password);
        
        return buffer;
    }

    /// <summary>
    /// Builds an auth response packet.
    /// </summary>
    public static byte[] BuildResponsePacket(bool success)
    {
        return new byte[] { (byte)PacketType.AuthResponse, success ? (byte)1 : (byte)0 };
    }

    /// <summary>
    /// Parses an auth request packet.
    /// </summary>
    public static bool TryParseRequestPacket(ReadOnlySpan<byte> data, out string password)
    {
        password = string.Empty;
        
        if (data.Length < 3) // Type + min string length
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            password = reader.ReadString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses an auth response packet.
    /// </summary>
    public static bool TryParseResponsePacket(ReadOnlySpan<byte> data, out bool success)
    {
        success = false;
        
        if (data.Length < 2)
            return false;

        success = data[1] != 0;
        return true;
    }
}

#endregion
