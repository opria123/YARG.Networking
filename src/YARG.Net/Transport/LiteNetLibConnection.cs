using System;
using System.Text;
using LiteNetLib;

namespace YARG.Net.Transport;

internal sealed class LiteNetLibConnection : INetConnection
{
    private readonly NetPeer _peer;
    private readonly Guid _id = Guid.NewGuid();

    public LiteNetLibConnection(NetPeer peer)
    {
        _peer = peer;
    }

    public Guid Id => _id;

    public string EndPoint => _peer.EndPoint?.ToString() ?? string.Empty;

    public void Disconnect(string? reason = null)
    {
        if (_peer.ConnectionState == ConnectionState.Disconnected)
        {
            return;
        }

        if (string.IsNullOrEmpty(reason))
        {
            _peer.Disconnect();
        }
        else
        {
            var payload = Encoding.UTF8.GetBytes(reason);
            _peer.Disconnect(payload);
        }
    }

    public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
    {
        if (payload.Length == 0)
        {
            return;
        }
        
        // Log every send attempt for debugging
        var peerState = _peer.ConnectionState;
        var firstByte = payload[0];
        TransportLogger.Log($"[LiteNetLibConnection.Send] Attempting send: length={payload.Length}, firstByte={firstByte}, peerState={peerState}, endpoint={EndPoint}");
        
        // Verify peer is still connected
        if (peerState != ConnectionState.Connected)
        {
            TransportLogger.Log($"[LiteNetLibConnection] WARNING: Send called but peer state is {peerState}, not Connected! Packet dropped (length={payload.Length}, firstByte={firstByte})");
            return;
        }

        var method = channel switch
        {
            ChannelType.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            ChannelType.ReliableSequenced => DeliveryMethod.ReliableSequenced,
            ChannelType.Unreliable => DeliveryMethod.Unreliable,
            _ => DeliveryMethod.ReliableOrdered,
        };

        var buffer = payload.ToArray();
        _peer.Send(buffer, method);
        TransportLogger.Log($"[LiteNetLibConnection.Send] Sent successfully: length={payload.Length}, firstByte={firstByte}, method={method}");
    }
}
