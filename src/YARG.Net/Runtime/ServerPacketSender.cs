using System;
using System.Collections.Generic;
using YARG.Net.Packets;
using YARG.Net.Transport;

namespace YARG.Net.Runtime;

/// <summary>
/// Helper class for sending packets from the server to clients.
/// Encapsulates common send patterns and packet building.
/// </summary>
public sealed class ServerPacketSender
{
    private readonly IServerConnectionManager _connectionManager;
    
    public ServerPacketSender(IServerConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }
    
    #region Core Send Methods
    
    /// <summary>
    /// Broadcasts a pre-built packet to all authenticated clients.
    /// </summary>
    public void Broadcast(byte[] packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        BroadcastSpan(packet.AsSpan(), channel);
    }
    
    /// <summary>
    /// Broadcasts a pre-built packet to all authenticated clients.
    /// </summary>
    public void BroadcastSpan(ReadOnlySpan<byte> packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        foreach (var kvp in _connectionManager.AuthenticatedClients)
        {
            try
            {
                kvp.Value.Connection.Send(packet.ToArray(), channel);
            }
            catch
            {
                // Ignore send errors - connection may have dropped
            }
        }
    }
    
    /// <summary>
    /// Broadcasts a pre-built packet to all authenticated clients except one.
    /// </summary>
    public void BroadcastExcept(byte[] packet, Guid excludeConnectionId, ChannelType channel = ChannelType.ReliableOrdered)
    {
        BroadcastExceptSpan(packet.AsSpan(), excludeConnectionId, channel);
    }
    
    /// <summary>
    /// Broadcasts a pre-built packet to all authenticated clients except one.
    /// </summary>
    public void BroadcastExceptSpan(ReadOnlySpan<byte> packet, Guid excludeConnectionId, ChannelType channel = ChannelType.ReliableOrdered)
    {
        foreach (var kvp in _connectionManager.AuthenticatedClients)
        {
            if (kvp.Key == excludeConnectionId)
                continue;
                
            try
            {
                kvp.Value.Connection.Send(packet.ToArray(), channel);
            }
            catch
            {
                // Ignore send errors - connection may have dropped
            }
        }
    }
    
    /// <summary>
    /// Sends a packet to a specific authenticated client by connection ID.
    /// </summary>
    public bool SendTo(Guid connectionId, byte[] packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        var client = _connectionManager.GetClient(connectionId);
        if (client is null)
            return false;
            
        try
        {
            client.Connection.Send(packet, channel);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Sends a packet to a specific client by player ID.
    /// </summary>
    public bool SendToPlayer(Guid playerId, byte[] packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        var client = _connectionManager.GetClientByPlayerId(playerId);
        if (client is null)
            return false;
            
        try
        {
            client.Connection.Send(packet, channel);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Sends a packet to a pending (unauthenticated) connection.
    /// Used for handshake responses and auth challenges.
    /// </summary>
    public bool SendToPending(Guid connectionId, byte[] packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        var connection = _connectionManager.GetConnection(connectionId);
        if (connection is null)
            return false;
            
        try
        {
            connection.Send(packet, channel);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Sends a packet to a specific connection.
    /// </summary>
    public bool SendToConnection(INetConnection connection, byte[] packet, ChannelType channel = ChannelType.ReliableOrdered)
    {
        if (connection is null)
            return false;
            
        try
        {
            connection.Send(packet, channel);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    #endregion
    
    #region Navigation Packets
    
    /// <summary>
    /// Broadcasts a navigation command to all clients.
    /// </summary>
    public void BroadcastNavigateToMenu(MenuTarget menuTarget)
    {
        var packet = NavigationBinaryPackets.BuildNavigatePacket(menuTarget);
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts a host disconnect notification to all clients.
    /// </summary>
    public void BroadcastHostDisconnect()
    {
        var packet = NavigationBinaryPackets.BuildHostDisconnectPacket();
        Broadcast(packet);
    }
    
    #endregion
    
    #region Lobby Packets
    
    /// <summary>
    /// Broadcasts the current lobby state to all clients.
    /// </summary>
    public void BroadcastLobbyState(bool isBrowsingSongs)
    {
        var packet = NavigationBinaryPackets.BuildLobbyStatePacket(isBrowsingSongs);
        Broadcast(packet);
    }
    
    /// <summary>
    /// Sends the current lobby state to a specific connection.
    /// </summary>
    public void SendLobbyStateTo(INetConnection connection, bool isBrowsingSongs)
    {
        var packet = NavigationBinaryPackets.BuildLobbyStatePacket(isBrowsingSongs);
        SendToConnection(connection, packet);
    }
    
    #endregion
    
    #region Ready State Packets
    
    /// <summary>
    /// Broadcasts a player's ready state to all clients.
    /// </summary>
    public void BroadcastPlayerReadyState(string playerName, bool isReady)
    {
        var packet = ReadyStateBinaryPackets.BuildReadyStatePacket(playerName, isReady);
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts that all players are ready.
    /// </summary>
    public void BroadcastAllPlayersReady()
    {
        var packet = ReadyStateBinaryPackets.BuildAllPlayersReadyPacket();
        Broadcast(packet);
    }
    
    #endregion
    
    #region Setlist Packets
    
    /// <summary>
    /// Broadcasts a setlist add notification to all clients.
    /// </summary>
    public void BroadcastSetlistAdd(string songHash, string playerName, string songName, string artistName)
    {
        var packet = SetlistBinaryPackets.BuildAddPacket(songHash, playerName, songName, artistName);
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts a setlist remove notification to all clients.
    /// </summary>
    public void BroadcastSetlistRemove(string songHash, string playerName, string songName, string artistName)
    {
        var packet = SetlistBinaryPackets.BuildRemovePacket(songHash, playerName, songName, artistName);
        Broadcast(packet);
    }
    
    /// <summary>
    /// Sends the current setlist sync to a specific connection.
    /// </summary>
    public void SendSetlistSyncTo(INetConnection connection, IReadOnlyList<SetlistEntry> entries)
    {
        var packet = SetlistBinaryPackets.BuildSyncPacket(entries);
        SendToConnection(connection, packet);
    }
    
    /// <summary>
    /// Broadcasts a setlist start command to all clients.
    /// </summary>
    public void BroadcastSetlistStart(IReadOnlyList<string> songHashes)
    {
        var packet = SetlistBinaryPackets.BuildStartPacket(songHashes);
        Broadcast(packet);
    }
    
    #endregion
    
    #region Gameplay Packets
    
    /// <summary>
    /// Broadcasts a start gameplay command to all clients.
    /// </summary>
    public void BroadcastStartGameplay()
    {
        var packet = GameplayBinaryPackets.BuildStartPacket();
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts a restart gameplay command to all clients.
    /// </summary>
    public void BroadcastRestartGameplay()
    {
        var packet = GameplayBinaryPackets.BuildRestartPacket();
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts a quit to library command to all clients.
    /// </summary>
    public void BroadcastQuitToLibrary()
    {
        var packet = GameplayBinaryPackets.BuildQuitToLibraryPacket();
        Broadcast(packet);
    }
    
    /// <summary>
    /// Broadcasts that a player has left during gameplay.
    /// </summary>
    public void BroadcastPlayerLeftGameplay(string playerName)
    {
        var packet = GameplayBinaryPackets.BuildPlayerLeftPacket(playerName);
        Broadcast(packet);
    }
    
    #endregion
    
    #region Score Packets
    
    /// <summary>
    /// Broadcasts a score screen advance command.
    /// </summary>
    public void BroadcastScoreScreenAdvance(int nextShowIndex)
    {
        var packet = ScoreBinaryPackets.BuildAdvancePacket(nextShowIndex);
        Broadcast(packet);
    }
    
    #endregion
    
    #region Unison Packets
    
    /// <summary>
    /// Broadcasts a unison bonus award to all clients.
    /// </summary>
    public void BroadcastUnisonBonusAward(double phraseStartTime)
    {
        var packet = UnisonBinaryPackets.BuildBonusAwardPacket(phraseStartTime);
        Broadcast(packet);
    }
    
    #endregion
    
    #region Shared Songs Packets
    
    /// <summary>
    /// Broadcasts a clear shared songs command to all clients.
    /// </summary>
    public void BroadcastClearSharedSongs()
    {
        var packet = new byte[] { (byte)PacketType.ClearSharedSongs };
        Broadcast(packet);
    }
    
    /// <summary>
    /// Sends shared songs in chunks to a specific connection.
    /// </summary>
    public void SendSharedSongsChunk(INetConnection connection, byte[] chunkData)
    {
        SendToConnection(connection, chunkData);
    }
    
    #endregion
    
    #region Authentication Packets
    
    /// <summary>
    /// Sends an authentication response to a pending client.
    /// </summary>
    public void SendAuthResponse(Guid connectionId, bool success)
    {
        var packet = AuthBinaryPackets.BuildResponsePacket(success);
        SendToPending(connectionId, packet);
    }
    
    #endregion
}
