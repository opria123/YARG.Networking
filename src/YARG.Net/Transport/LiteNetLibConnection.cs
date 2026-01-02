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

    // In LiteNetLib 1.2+, NetPeer derives from IPEndPoint so we use it directly
    public string EndPoint => _peer.ToString();

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
        
        var peerState = _peer.ConnectionState;
        
        // Verify peer is still connected
        if (peerState != ConnectionState.Connected)
        {
            TransportLogger.Log($"[LiteNetLibConnection] WARNING: Send dropped - peer state is {peerState} (len={payload.Length})");
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
        
        // Verbose per-packet logging (disabled by default)
        if (TransportLogger.VerboseLogging)
        {
            TransportLogger.LogVerbose($"[LiteNetLibConnection] Sent: len={payload.Length}, type={payload[0]}, method={method}");
        }
    }
}
