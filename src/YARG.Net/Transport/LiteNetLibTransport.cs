using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;

namespace YARG.Net.Transport;

/// <summary>
/// Static logging helper for the transport layer.
/// Set LogAction from Unity side to capture logs.
/// </summary>
public static class TransportLogger
{
    /// <summary>
    /// Set this delegate from Unity to capture transport-layer logs.
    /// </summary>
    public static Action<string>? LogAction { get; set; }
    
    /// <summary>
    /// Log a message. Goes to LogAction if set, otherwise Console.WriteLine.
    /// </summary>
    public static void Log(string message)
    {
        if (LogAction != null)
        {
            LogAction(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}

/// <summary>
/// LiteNetLib-backed implementation of <see cref="INetTransport"/> for production networking.
/// </summary>
public sealed class LiteNetLibTransport : INetTransport, INetEventListener
{
    private readonly ConcurrentDictionary<NetPeer, LiteNetLibConnection> _connections = new();
    private NetManager? _netManager;

    public event Action<INetConnection>? OnPeerConnected;
    public event Action<INetConnection>? OnPeerDisconnected;
    public event Action<INetConnection, ReadOnlyMemory<byte>, ChannelType>? OnPayloadReceived;
    
    /// <summary>
    /// Fired when an unconnected message is received (used for discovery).
    /// Parameters: remoteEndPoint, data
    /// </summary>
    public event Action<IPEndPoint, byte[]>? OnUnconnectedMessage;
    
    /// <summary>
    /// Fired when latency to a peer is updated.
    /// Parameters: connection, latencyMs
    /// </summary>
    public event Action<INetConnection, int>? OnLatencyUpdate;

    public bool IsRunning => _netManager?.IsRunning == true;
    
    /// <summary>
    /// Gets the underlying NetManager for advanced operations (discovery, etc).
    /// </summary>
    public NetManager? NetManager => _netManager;

    public void Start(TransportStartOptions options)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("LiteNetLib transport already started.");
        }

        _netManager = new NetManager(this)
        {
            NatPunchEnabled = options.EnableNatPunchThrough,
            UnconnectedMessagesEnabled = true, // Required for discovery
            BroadcastReceiveEnabled = true, // Enable broadcast reception for LAN discovery
            ReuseAddress = true, // Allow multiple processes on same machine to bind (important for ParrelSync testing)
            EnableStatistics = true, // Enable packet statistics for debugging
        };

        bool started = options.IsServer
            ? _netManager.Start(options.Port)
            : _netManager.Start();

        if (!started)
        {
            _netManager = null;
            throw new InvalidOperationException("LiteNetLib failed to start with the provided options.");
        }

        if (!options.IsServer)
        {
            if (string.IsNullOrWhiteSpace(options.Address))
            {
                throw new ArgumentException("Client mode requires a destination address.", nameof(options));
            }

            var peer = _netManager.Connect(options.Address, options.Port, string.Empty);
            if (peer is null)
            {
                Shutdown("Unable to create LiteNetLib client peer.");
                throw new InvalidOperationException("LiteNetLib failed to initiate the client connection.");
            }
        }
    }

    private static long _pollCount = 0;
    private static DateTime _lastPollLog = DateTime.MinValue;

    public void Poll(TimeSpan timeout)
    {
        if (_netManager is null)
        {
            return;
        }

        // Log poll status every 10 seconds to confirm polling is active
        _pollCount++;
        var now = DateTime.UtcNow;
        if ((now - _lastPollLog).TotalSeconds >= 10)
        {
            // Enable statistics temporarily to see packet counts
            var stats = _netManager.Statistics;
            TransportLogger.Log($"[LiteNetLibTransport.Poll] Poll active: count={_pollCount}, connectedPeers={_netManager.ConnectedPeersCount}, isRunning={_netManager.IsRunning}, unconnectedEnabled={_netManager.UnconnectedMessagesEnabled}, reuseAddr={_netManager.ReuseAddress}, packetsReceived={stats.PacketsReceived}, bytesReceived={stats.BytesReceived}");
            _lastPollLog = now;
        }

        if (timeout <= TimeSpan.Zero)
        {
            _netManager.PollEvents();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        do
        {
            _netManager.PollEvents();

            if (stopwatch.Elapsed >= timeout)
            {
                break;
            }

            Thread.Sleep(1);
        } while (true);
    }

    public void Shutdown(string? reason = null)
    {
        if (_netManager is null)
        {
            return;
        }

        foreach (var peer in _netManager.ConnectedPeerList)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                peer.Disconnect(Encoding.UTF8.GetBytes(reason));
            }
            else
            {
                peer.Disconnect();
            }
        }

        _netManager.Stop();
        _netManager = null;
        _connections.Clear();
    }

    public void Dispose()
    {
        Shutdown();
    }

    private LiteNetLibConnection GetOrAddConnection(NetPeer peer)
    {
        return _connections.GetOrAdd(peer, static netPeer => new LiteNetLibConnection(netPeer));
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        TransportLogger.Log($"[LiteNetLibTransport] OnPeerConnected: {peer.EndPoint}, connectedPeers={_netManager?.ConnectedPeersCount ?? -1}, unconnectedEnabled={_netManager?.UnconnectedMessagesEnabled ?? false}");
        var connection = GetOrAddConnection(peer);
        OnPeerConnected?.Invoke(connection);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_connections.TryRemove(peer, out var connection))
        {
            OnPeerDisconnected?.Invoke(connection);
        }
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        // Log network errors - these could indicate why packets aren't being received
        TransportLogger.Log($"[LiteNetLibTransport] OnNetworkError from {endPoint}, error={socketError}");
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var connection = GetOrAddConnection(peer);
        var payload = reader.GetRemainingBytes();
        reader.Recycle();
        
        // Log every packet received at transport level
        var firstByte = payload.Length > 0 ? payload[0] : (byte)0;
        TransportLogger.Log($"[LiteNetLibTransport.OnNetworkReceive] Received packet: length={payload.Length}, firstByte={firstByte}, from={peer.EndPoint}, channel={deliveryMethod}");

        var channel = deliveryMethod switch
        {
            DeliveryMethod.ReliableOrdered => ChannelType.ReliableOrdered,
            DeliveryMethod.ReliableSequenced => ChannelType.ReliableSequenced,
            DeliveryMethod.Sequenced => ChannelType.ReliableSequenced,
            DeliveryMethod.Unreliable => ChannelType.Unreliable,
            DeliveryMethod.ReliableUnordered => ChannelType.ReliableOrdered,
            _ => ChannelType.Unreliable,
        };

        OnPayloadReceived?.Invoke(connection, payload, channel);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        try
        {
            var data = reader.GetRemainingBytes();
            reader.Recycle();
            TransportLogger.Log($"[LiteNetLibTransport] OnNetworkReceiveUnconnected from {remoteEndPoint}, type={messageType}, bytes={data.Length}, hasSubscribers={OnUnconnectedMessage != null}, connectedPeers={_netManager?.ConnectedPeersCount ?? -1}");
            OnUnconnectedMessage?.Invoke(remoteEndPoint, data);
        }
        catch (Exception ex)
        {
            TransportLogger.Log($"[LiteNetLibTransport] ERROR in OnNetworkReceiveUnconnected: {ex.Message}\n{ex.StackTrace}");
        }
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        if (_connections.TryGetValue(peer, out var connection))
        {
            OnLatencyUpdate?.Invoke(connection, latency);
        }
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(string.Empty);
    }
}
