using System;
using LiteNetLib;
using YARG.Net.Transport;

namespace YARG.Net.Relay;

/// <summary>
/// Wraps a LiteNetRelayClient to present it as an INetConnection.
/// This allows the relay to be used transparently by the networking layer.
/// </summary>
public sealed class RelayConnection : INetConnection
{
    private readonly LiteNetRelayClient _relayClient;
    private readonly Guid _id;
    
    /// <summary>
    /// Creates a new relay connection wrapper.
    /// </summary>
    /// <param name="relayClient">The underlying relay client.</param>
    public RelayConnection(LiteNetRelayClient relayClient)
    {
        _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
        _id = Guid.NewGuid();
    }
    
    /// <summary>
    /// Gets the unique connection ID.
    /// </summary>
    public Guid Id => _id;
    
    /// <summary>
    /// Gets the endpoint description (relay server info).
    /// </summary>
    public string EndPoint => $"Relay:{_relayClient.SessionId}";
    
    /// <summary>
    /// Gets the underlying relay client.
    /// </summary>
    public LiteNetRelayClient RelayClient => _relayClient;
    
    /// <summary>
    /// Disconnects from the relay.
    /// </summary>
    /// <param name="reason">Optional disconnect reason.</param>
    public void Disconnect(string? reason = null)
    {
        _relayClient.Disconnect();
    }
    
    /// <summary>
    /// Sends data through the relay to the other peer.
    /// </summary>
    /// <param name="payload">Data to send.</param>
    /// <param name="channel">Channel type (mapped to LiteNetLib delivery method).</param>
    public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
    {
        if (payload.Length == 0)
        {
            return;
        }
        
        var method = channel switch
        {
            ChannelType.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            ChannelType.ReliableSequenced => DeliveryMethod.ReliableSequenced,
            ChannelType.Unreliable => DeliveryMethod.Unreliable,
            _ => DeliveryMethod.ReliableOrdered,
        };
        
        _relayClient.Send(payload.ToArray(), method);
    }
}
