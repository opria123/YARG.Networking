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
    /// Enable verbose logging (per-packet logs). Default false for production.
    /// </summary>
    public static bool VerboseLogging { get; set; } = false;
    
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
    
    /// <summary>
    /// Log a verbose message (only if VerboseLogging is enabled).
    /// Use for per-packet and high-frequency logs.
    /// </summary>
    public static void LogVerbose(string message)
    {
        if (VerboseLogging)
        {
            Log(message);
        }
    }
}

/// <summary>
/// LiteNetLib-backed implementation of <see cref="INetTransport"/> for production networking.
/// Also implements INatPunchListener for NAT hole punching coordination.
/// </summary>
public sealed class LiteNetLibTransport : INetTransport, INetEventListener, INatPunchListener
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
    
    /// <summary>
    /// Fired when NAT punch succeeds on this transport.
    /// Parameters: targetEndPoint, addressType, token
    /// </summary>
    public event Action<IPEndPoint, NatAddressType, string>? OnNatPunchSuccess;

    public bool IsRunning => _netManager?.IsRunning == true;
    
    /// <summary>
    /// Gets the local port this transport is bound to.
    /// </summary>
    public int LocalPort => _netManager?.LocalPort ?? 0;
    
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
            IPv6Enabled = false, // Force IPv4 to ensure compatibility with punch server
        };
        
        // Initialize NAT punch module with this transport as the listener
        _netManager.NatPunchModule.Init(this);

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

    // Instance-level poll tracking (was static - unsafe for multiple instances)
    private long _pollCount = 0;
    private DateTime _lastPollLog = DateTime.MinValue;

    public void Poll(TimeSpan timeout)
    {
        if (_netManager is null)
        {
            return;
        }

        // Log poll status every 10 seconds (verbose only)
        _pollCount++;
        var now = DateTime.UtcNow;
        if (TransportLogger.VerboseLogging && (now - _lastPollLog).TotalSeconds >= 10)
        {
            var stats = _netManager.Statistics;
            TransportLogger.LogVerbose($"[LiteNetLibTransport.Poll] Poll active: count={_pollCount}, connectedPeers={_netManager.ConnectedPeersCount}, packetsReceived={stats.PacketsReceived}, bytesReceived={stats.BytesReceived}");
            _lastPollLog = now;
        }

        if (timeout <= TimeSpan.Zero)
        {
            _netManager.PollEvents();
            _netManager.NatPunchModule.PollEvents(); // Poll NAT punch events too
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        do
        {
            _netManager.PollEvents();
            _netManager.NatPunchModule.PollEvents(); // Poll NAT punch events too

            if (stopwatch.Elapsed >= timeout)
            {
                break;
            }

            Thread.Sleep(1);
        } while (true);
    }
    
    /// <summary>
    /// Sends a NAT introduction request to a punch server.
    /// Use this to initiate NAT punch-through to a host.
    /// </summary>
    /// <param name="punchServerHost">The hostname or IP of the punch server</param>
    /// <param name="punchServerPort">The UDP port of the punch server</param>
    /// <param name="token">The punch token (identifying the session)</param>
    public void SendNatIntroduceRequest(string punchServerHost, int punchServerPort, string token)
    {
        if (_netManager == null || !_netManager.IsRunning)
        {
            TransportLogger.Log("[LiteNetLibTransport] Cannot send NAT introduce request: not running");
            return;
        }
        
        // CRITICAL: Explicitly resolve to IPv4 address to avoid silent failures
        // when IPv6Enabled=false. LiteNetLib's SendNatIntroduceRequest uses NetUtils.MakeEndPoint
        // which may resolve to IPv6 if the system has IPv6 support, but if our NetManager
        // has IPv6Enabled=false, the _udpSocketv6 is null and SendRawCore silently returns 0.
        IPEndPoint punchServerEndpoint;
        try
        {
            if (IPAddress.TryParse(punchServerHost, out var ipAddress))
            {
                // Already an IP address
                punchServerEndpoint = new IPEndPoint(ipAddress, punchServerPort);
            }
            else
            {
                // Resolve DNS - prefer IPv4 to match IPv6Enabled=false
                var addresses = Dns.GetHostAddresses(punchServerHost);
                var ipv4Address = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
                
                if (ipv4Address == null)
                {
                    TransportLogger.Log($"[LiteNetLibTransport] ERROR: Could not resolve {punchServerHost} to IPv4 address. Available: {string.Join(", ", Array.ConvertAll(addresses, a => $"{a} ({a.AddressFamily})"))}");
                    return;
                }
                
                punchServerEndpoint = new IPEndPoint(ipv4Address, punchServerPort);
                TransportLogger.Log($"[LiteNetLibTransport] Resolved {punchServerHost} to IPv4: {ipv4Address}");
            }
        }
        catch (Exception ex)
        {
            TransportLogger.Log($"[LiteNetLibTransport] ERROR resolving punch server address {punchServerHost}: {ex.Message}");
            return;
        }
        
        TransportLogger.Log($"[LiteNetLibTransport] Sending NAT introduce request to {punchServerEndpoint} with token={token}");
        
        // DEBUG: Also send a raw UDP packet to verify network connectivity
        // This bypasses LiteNetLib entirely to test if the issue is with LiteNetLib or the network
        try
        {
            using var rawUdp = new System.Net.Sockets.UdpClient();
            var debugMessage = Encoding.UTF8.GetBytes($"YARG-DEBUG-PUNCH:{token}");
            int sent = rawUdp.Send(debugMessage, debugMessage.Length, punchServerEndpoint);
            TransportLogger.Log($"[LiteNetLibTransport] DEBUG: Raw UDP sent {sent} bytes to {punchServerEndpoint}");
        }
        catch (Exception ex)
        {
            TransportLogger.Log($"[LiteNetLibTransport] DEBUG: Raw UDP send failed: {ex.Message}");
        }
        
        _netManager.NatPunchModule.SendNatIntroduceRequest(punchServerEndpoint, token);
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
        // In LiteNetLib 1.2+, NetPeer derives from IPEndPoint so we use it directly
        TransportLogger.Log($"[LiteNetLibTransport] Peer connected: {peer}");
        var connection = GetOrAddConnection(peer);
        OnPeerConnected?.Invoke(connection);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        TransportLogger.Log($"[LiteNetLibTransport] Peer disconnected: {peer}, reason={disconnectInfo.Reason}, additionalInfo={disconnectInfo.AdditionalData?.AvailableBytes ?? 0} bytes");
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
        
        // Verbose per-packet logging (disabled by default for performance)
        if (TransportLogger.VerboseLogging)
        {
            var firstByte = payload.Length > 0 ? payload[0] : (byte)0;
            TransportLogger.LogVerbose($"[LiteNetLibTransport] Received packet: len={payload.Length}, type={firstByte}, from={peer}");
        }

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
            TransportLogger.LogVerbose($"[LiteNetLibTransport] Unconnected message from {remoteEndPoint}, type={messageType}, bytes={data.Length}");
            OnUnconnectedMessage?.Invoke(remoteEndPoint, data);
        }
        catch (Exception ex)
        {
            TransportLogger.Log($"[LiteNetLibTransport] ERROR in OnNetworkReceiveUnconnected: {ex.Message}");
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
    
    // ========== INatPunchListener Implementation ==========
    
    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        // This is only called on punch servers, not on game clients/servers
        TransportLogger.Log($"[LiteNetLibTransport] NAT introduction request (unexpected): local={localEndPoint}, remote={remoteEndPoint}, token={token}");
    }
    
    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        TransportLogger.Log($"[LiteNetLibTransport] NAT punch SUCCESS: target={targetEndPoint}, type={type}, token={token}");
        OnNatPunchSuccess?.Invoke(targetEndPoint, type, token);
    }
}
