using System;
using System.Collections.Generic;
using YARG.Net.Packets;
using YARG.Net.Sessions;
using YARG.Net.Transport;

namespace YARG.Net.Handlers.Server;

/// <summary>
/// Handles binary packet relay for the dedicated server.
/// Binary packets use the first byte as the PacketType enum value.
/// This handler relays gameplay-related packets to other connected clients.
/// </summary>
public sealed class ServerBinaryPacketRelay
{
    private readonly SessionManager _sessionManager;
    
    /// <summary>
    /// Set of packet types that should be relayed to all other clients (excluding sender).
    /// </summary>
    private static readonly HashSet<PacketType> _relayToOthersPacketTypes = new()
    {
        PacketType.GameplayState,      // Live gameplay snapshots (score, combo, etc.)
        PacketType.UnisonPhraseHit,    // Unison phrase notifications
        PacketType.ScoreResults,       // Final score results
        PacketType.LobbyReadyState,    // Player ready state changes
        PacketType.PlayerPresetSync,   // Player visual presets
        PacketType.BandScoreUpdate,    // Band score updates
    };
    
    /// <summary>
    /// Set of packet types that should be broadcast to ALL clients (including sender).
    /// </summary>
    private static readonly HashSet<PacketType> _broadcastToAllPacketTypes = new()
    {
        PacketType.UnisonBonusAward,   // Unison bonus awards (host sends to all)
    };

    /// <summary>
    /// Raised when a binary packet is received that needs custom handling.
    /// </summary>
    public event EventHandler<BinaryPacketReceivedEventArgs>? PacketReceived;

    public ServerBinaryPacketRelay(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// Attempts to handle a binary packet. Returns true if the packet was handled.
    /// </summary>
    /// <param name="connection">The connection that sent the packet.</param>
    /// <param name="payload">The raw packet data.</param>
    /// <param name="channel">The channel the packet was received on.</param>
    /// <returns>True if the packet was handled as a binary packet.</returns>
    public bool TryHandleBinaryPacket(INetConnection connection, ReadOnlyMemory<byte> payload, ChannelType channel)
    {
        if (payload.Length < 1)
            return false;

        var packetTypeByte = payload.Span[0];
        
        // JSON packets start with '{' (byte 123) or '[' (byte 91)
        // We need to distinguish these from binary packets
        if (packetTypeByte == (byte)'{' || packetTypeByte == (byte)'[')
            return false;

        // Check if this is a valid PacketType enum value
        if (!Enum.IsDefined(typeof(PacketType), (int)packetTypeByte))
            return false;

        var packetType = (PacketType)packetTypeByte;

        // Raise event for any interested listeners
        PacketReceived?.Invoke(this, new BinaryPacketReceivedEventArgs(connection, packetType, payload, channel));

        // Check if this packet type should be relayed to others
        if (_relayToOthersPacketTypes.Contains(packetType))
        {
            RelayToOthers(connection, payload, channel);
            return true;
        }

        // Check if this packet type should be broadcast to all
        if (_broadcastToAllPacketTypes.Contains(packetType))
        {
            BroadcastToAll(payload, channel);
            return true;
        }

        // Packet wasn't handled by relay - let other handlers process it
        return false;
    }

    /// <summary>
    /// Relays a packet to all connected clients except the sender.
    /// </summary>
    private void RelayToOthers(INetConnection sourceConnection, ReadOnlyMemory<byte> payload, ChannelType channel)
    {
        var sessions = _sessionManager.GetSessionsSnapshot();
        var data = payload.Span;
        
        foreach (var session in sessions)
        {
            if (session.ConnectionId != sourceConnection.Id && session.Connection != null)
            {
                try
                {
                    session.Connection.Send(data, channel);
                }
                catch (Exception)
                {
                    // Ignore send failures - connection may have disconnected
                }
            }
        }
    }

    /// <summary>
    /// Broadcasts a packet to all connected clients.
    /// </summary>
    private void BroadcastToAll(ReadOnlyMemory<byte> payload, ChannelType channel)
    {
        var sessions = _sessionManager.GetSessionsSnapshot();
        var data = payload.Span;
        
        foreach (var session in sessions)
        {
            if (session.Connection != null)
            {
                try
                {
                    session.Connection.Send(data, channel);
                }
                catch (Exception)
                {
                    // Ignore send failures - connection may have disconnected
                }
            }
        }
    }
}

/// <summary>
/// Event args for binary packet received events.
/// </summary>
public sealed class BinaryPacketReceivedEventArgs : EventArgs
{
    public INetConnection Connection { get; }
    public PacketType PacketType { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ChannelType Channel { get; }

    public BinaryPacketReceivedEventArgs(INetConnection connection, PacketType packetType, ReadOnlyMemory<byte> payload, ChannelType channel)
    {
        Connection = connection;
        PacketType = packetType;
        Payload = payload;
        Channel = channel;
    }
}
