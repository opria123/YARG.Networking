using System;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by the host to navigate all clients to a specific menu.
/// </summary>
public sealed record NavigateToMenuPacket(
    Guid LobbyId,
    MenuTarget Target) : IPacketPayload;

/// <summary>
/// Sent by the host to notify clients that the host is disconnecting gracefully.
/// </summary>
public sealed record HostDisconnectPacket(
    Guid LobbyId,
    string Reason) : IPacketPayload;

/// <summary>
/// Sent when gameplay should be restarted.
/// </summary>
public sealed record GameplayRestartPacket(
    Guid LobbyId) : IPacketPayload;

/// <summary>
/// Sent when a player leaves during gameplay.
/// </summary>
public sealed record PlayerLeftGameplayPacket(
    Guid LobbyId,
    string PlayerName) : IPacketPayload;

/// <summary>
/// Sent when the host quits gameplay and all players should return to the music library.
/// </summary>
public sealed record QuitToLibraryPacket(
    Guid LobbyId) : IPacketPayload;

/// <summary>
/// Menu navigation targets.
/// </summary>
public enum MenuTarget
{
    /// <summary>
    /// No specific menu target.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Music library / song browser.
    /// </summary>
    MusicLibrary = 1,
    
    /// <summary>
    /// Lobby room.
    /// </summary>
    LobbyRoom = 2,
    
    /// <summary>
    /// Difficulty selection screen.
    /// </summary>
    DifficultySelect = 3,
}

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for navigation-related messages.
/// </summary>
public static class NavigationBinaryPackets
{
    /// <summary>
    /// Builds a navigate to menu packet.
    /// </summary>
    public static byte[] BuildNavigatePacket(MenuTarget target)
    {
        return new byte[] { (byte)PacketType.NavigateToMenu, (byte)target };
    }

    /// <summary>
    /// Parses a navigate to menu packet.
    /// </summary>
    public static bool TryParseNavigatePacket(ReadOnlySpan<byte> data, out MenuTarget target)
    {
        target = MenuTarget.None;
        
        if (data.Length < 2)
            return false;

        target = (MenuTarget)data[1];
        return true;
    }

    /// <summary>
    /// Builds a host disconnect packet.
    /// </summary>
    public static byte[] BuildHostDisconnectPacket()
    {
        return new byte[] { (byte)PacketType.HostDisconnect };
    }

    /// <summary>
    /// Builds a lobby state packet.
    /// </summary>
    public static byte[] BuildLobbyStatePacket(bool isBrowsing)
    {
        return new byte[] { (byte)PacketType.LobbyState, isBrowsing ? (byte)1 : (byte)0 };
    }

    /// <summary>
    /// Parses a lobby state packet.
    /// </summary>
    public static bool TryParseLobbyStatePacket(ReadOnlySpan<byte> data, out bool isBrowsing)
    {
        isBrowsing = false;
        
        if (data.Length < 2)
            return false;

        isBrowsing = data[1] != 0;
        return true;
    }
}

#endregion
