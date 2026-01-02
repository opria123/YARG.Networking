using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace YARG.Net.Relay;

/// <summary>
/// LiteNetLib-based relay client for connecting through a relay server.
/// Used when direct P2P connections are not possible.
/// Both host and client use this to connect to the relay server.
/// </summary>
public sealed class LiteNetRelayClient : INetEventListener, IDisposable
{
    private readonly string _relayAddress;
    private readonly int _relayPort;
    private readonly Guid _sessionId;
    private readonly bool _isHost;
    
    private NetManager? _client;
    private NetPeer? _relayPeer;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    
    private bool _isRegistered;
    private bool _isPeerConnected;
    private bool _disposed;
    
    // Relay opcodes (must match server)
    private const byte OPCODE_REGISTER = 1;
    private const byte OPCODE_DATA = 2;
    private const byte OPCODE_REGISTERED = 10;
    private const byte OPCODE_PEER_CONNECTED = 11;
    private const byte OPCODE_PEER_DISCONNECTED = 12;
    private const byte OPCODE_ERROR = 20;
    
    /// <summary>Event fired when data is received from the other peer via relay.</summary>
    public event Action<byte[], DeliveryMethod>? OnDataReceived;
    
    /// <summary>Event fired when registered with the relay server.</summary>
    public event Action? OnRegistered;
    
    /// <summary>Event fired when the other peer connects via relay.</summary>
    public event Action? OnRelayPeerConnected;
    
    /// <summary>Event fired when the other peer disconnects.</summary>
    public event Action? OnRelayPeerDisconnected;
    
    /// <summary>Event fired when disconnected from relay.</summary>
    public event Action<string>? OnDisconnected;
    
    /// <summary>Event fired on errors.</summary>
    public event Action<string>? OnError;
    
    /// <summary>Gets whether connected to the relay server.</summary>
    public bool IsConnectedToRelay => _relayPeer != null && _relayPeer.ConnectionState == ConnectionState.Connected;
    
    /// <summary>Gets whether registered with the relay as host/client.</summary>
    public bool IsRegistered => _isRegistered;
    
    /// <summary>Gets whether the other peer is connected via relay.</summary>
    public bool IsPeerConnected => _isPeerConnected;
    
    /// <summary>Gets whether this client is the host side.</summary>
    public bool IsHost => _isHost;
    
    /// <summary>Gets the session ID.</summary>
    public Guid SessionId => _sessionId;
    
    /// <summary>
    /// Creates a new relay client.
    /// </summary>
    /// <param name="relayAddress">Relay server address.</param>
    /// <param name="relayPort">Relay server port.</param>
    /// <param name="sessionId">Session ID from relay allocation.</param>
    /// <param name="isHost">True if this is the host side.</param>
    public LiteNetRelayClient(string relayAddress, int relayPort, Guid sessionId, bool isHost)
    {
        _relayAddress = relayAddress;
        _relayPort = relayPort;
        _sessionId = sessionId;
        _isHost = isHost;
    }
    
    /// <summary>
    /// Connects to the relay server and registers as host or client.
    /// </summary>
    /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
    /// <returns>True if connected and registered successfully.</returns>
    public async Task<bool> ConnectAsync(int timeoutMs = 10000)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiteNetRelayClient));
        
        _client = new NetManager(this)
        {
            AutoRecycle = true,
            DisconnectTimeout = 30000,
            UpdateTime = 15,
            UnsyncedEvents = false,
            IPv6Enabled = false,
        };
        
        if (!_client.Start())
        {
            OnError?.Invoke("Failed to start relay client");
            return false;
        }
        
        Console.WriteLine($"[LiteNetRelayClient] Connecting to relay at {_relayAddress}:{_relayPort}...");
        
        // Connect to relay server
        _relayPeer = _client.Connect(_relayAddress, _relayPort, string.Empty);
        
        if (_relayPeer == null)
        {
            OnError?.Invoke("Failed to initiate connection to relay");
            return false;
        }
        
        // Start polling
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollCts.Token));
        
        // Wait for connection
        var startTime = DateTime.UtcNow;
        while (!IsConnectedToRelay && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            await Task.Delay(50);
        }
        
        if (!IsConnectedToRelay)
        {
            OnError?.Invoke("Connection to relay timed out");
            return false;
        }
        
        Console.WriteLine($"[LiteNetRelayClient] Connected to relay, registering as {(_isHost ? "host" : "client")}...");
        
        // Send registration
        var writer = new NetDataWriter();
        writer.Put(OPCODE_REGISTER);
        writer.Put(_sessionId.ToByteArray());
        writer.Put(_isHost);
        _relayPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        
        // Wait for registration confirmation
        startTime = DateTime.UtcNow;
        while (!_isRegistered && (DateTime.UtcNow - startTime).TotalMilliseconds < 5000)
        {
            await Task.Delay(50);
        }
        
        if (!_isRegistered)
        {
            OnError?.Invoke("Registration with relay timed out");
            return false;
        }
        
        Console.WriteLine($"[LiteNetRelayClient] Registered with relay as {(_isHost ? "host" : "client")} for session {_sessionId}");
        
        return true;
    }
    
    /// <summary>
    /// Sends data to the other peer through the relay.
    /// </summary>
    /// <param name="data">Data to send.</param>
    /// <param name="deliveryMethod">Delivery method.</param>
    public void Send(byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
    {
        if (!IsConnectedToRelay || _relayPeer == null)
        {
            return;
        }
        
        var writer = new NetDataWriter();
        writer.Put(OPCODE_DATA);
        writer.Put(data);
        _relayPeer.Send(writer, deliveryMethod);
    }
    
    /// <summary>
    /// Sends data using a NetDataWriter through the relay.
    /// </summary>
    public void Send(NetDataWriter dataWriter, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
    {
        Send(dataWriter.CopyData(), deliveryMethod);
    }
    
    /// <summary>
    /// Disconnects from the relay.
    /// </summary>
    public void Disconnect()
    {
        _pollCts?.Cancel();
        _relayPeer?.Disconnect();
        _client?.Stop();
        _isRegistered = false;
        _isPeerConnected = false;
    }
    
    private void PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _client != null)
        {
            try
            {
                _client.PollEvents();
                Thread.Sleep(15);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiteNetRelayClient] Poll error: {ex.Message}");
            }
        }
    }
    
    // INetEventListener implementation
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        // We're a client, reject incoming connections
        request.Reject();
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"[LiteNetRelayClient] Connected to relay server");
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"[LiteNetRelayClient] Disconnected from relay: {disconnectInfo.Reason}");
        _isRegistered = false;
        _isPeerConnected = false;
        OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
    }
    
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Console.WriteLine($"[LiteNetRelayClient] Network error: {socketError}");
        OnError?.Invoke($"Network error: {socketError}");
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes < 1)
        {
            reader.Recycle();
            return;
        }
        
        var opcode = reader.GetByte();
        
        switch (opcode)
        {
            case OPCODE_REGISTERED:
                HandleRegistered(reader);
                break;
                
            case OPCODE_PEER_CONNECTED:
                HandlePeerConnected(reader);
                break;
                
            case OPCODE_PEER_DISCONNECTED:
                HandlePeerDisconnected(reader);
                break;
                
            case OPCODE_DATA:
                HandleData(reader, deliveryMethod);
                break;
                
            case OPCODE_ERROR:
                HandleError(reader);
                break;
                
            default:
                Console.WriteLine($"[LiteNetRelayClient] Unknown opcode: {opcode}");
                break;
        }
        
        reader.Recycle();
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        reader.Recycle();
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Could track latency to relay if needed
    }
    
    private void HandleRegistered(NetPacketReader reader)
    {
        if (reader.AvailableBytes < 17)
            return;
        
        var sessionIdBytes = new byte[16];
        reader.GetBytes(sessionIdBytes, 16);
        var sessionId = new Guid(sessionIdBytes);
        var isHost = reader.GetBool();
        
        if (sessionId == _sessionId)
        {
            _isRegistered = true;
            Console.WriteLine($"[LiteNetRelayClient] Registration confirmed for session {sessionId}");
            OnRegistered?.Invoke();
        }
    }
    
    private void HandlePeerConnected(NetPacketReader reader)
    {
        if (reader.AvailableBytes < 16)
            return;
        
        var sessionIdBytes = new byte[16];
        reader.GetBytes(sessionIdBytes, 16);
        var sessionId = new Guid(sessionIdBytes);
        
        if (sessionId == _sessionId)
        {
            _isPeerConnected = true;
            Console.WriteLine($"[LiteNetRelayClient] Peer connected via relay for session {sessionId}");
            OnRelayPeerConnected?.Invoke();
        }
    }
    
    private void HandlePeerDisconnected(NetPacketReader reader)
    {
        if (reader.AvailableBytes < 16)
            return;
        
        var sessionIdBytes = new byte[16];
        reader.GetBytes(sessionIdBytes, 16);
        var sessionId = new Guid(sessionIdBytes);
        
        if (sessionId == _sessionId)
        {
            _isPeerConnected = false;
            Console.WriteLine($"[LiteNetRelayClient] Peer disconnected from relay session {sessionId}");
            OnRelayPeerDisconnected?.Invoke();
        }
    }
    
    private void HandleData(NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes == 0)
            return;
        
        var data = new byte[reader.AvailableBytes];
        reader.GetBytes(data, data.Length);
        
        OnDataReceived?.Invoke(data, deliveryMethod);
    }
    
    private void HandleError(NetPacketReader reader)
    {
        if (reader.AvailableBytes < 16)
            return;
        
        var sessionIdBytes = new byte[16];
        reader.GetBytes(sessionIdBytes, 16);
        var message = reader.GetString();
        
        Console.WriteLine($"[LiteNetRelayClient] Error from relay: {message}");
        OnError?.Invoke(message);
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        Disconnect();
        _pollCts?.Dispose();
        _client = null;
    }
}
