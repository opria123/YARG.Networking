using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Relay;

/// <summary>
/// Relay client that communicates through a relay server when direct P2P is not possible.
/// Handles the relay protocol for both hosts and clients.
/// </summary>
public sealed class RelayClient : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _relayEndpoint;
    private readonly Guid _sessionId;
    private readonly bool _isHost;
    private readonly CancellationTokenSource _cts = new();
    
    private bool _isRegistered;
    private bool _peerConnected;
    private Task? _receiveTask;
    private DateTime _lastHeartbeat = DateTime.MinValue;
    
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    
    /// <summary>Event fired when data is received from the peer.</summary>
    public event Action<byte[]>? OnDataReceived;
    
    /// <summary>Event fired when the other peer connects.</summary>
    public event Action? OnPeerConnected;
    
    /// <summary>Event fired when the other peer disconnects.</summary>
    public event Action? OnPeerDisconnected;
    
    /// <summary>Event fired when registration with the relay succeeds.</summary>
    public event Action? OnRegistered;
    
    /// <summary>Event fired on errors.</summary>
    public event Action<string>? OnError;
    
    public bool IsRegistered => _isRegistered;
    public bool IsPeerConnected => _peerConnected;
    public Guid SessionId => _sessionId;
    
    public RelayClient(string relayHost, int relayPort, Guid sessionId, bool isHost)
    {
        _sessionId = sessionId;
        _isHost = isHost;
        _relayEndpoint = new IPEndPoint(
            Dns.GetHostAddresses(relayHost)[0], 
            relayPort);
        
        _udpClient = new UdpClient();
        _udpClient.Client.ReceiveBufferSize = 256 * 1024;
        _udpClient.Client.SendBufferSize = 256 * 1024;
    }
    
    /// <summary>
    /// Starts the relay client and registers with the server.
    /// </summary>
    public void Start()
    {
        _receiveTask = Task.Run(ReceiveLoopAsync);
        
        // Send registration
        SendRegistration();
    }
    
    /// <summary>
    /// Sends data through the relay to the peer.
    /// </summary>
    public void Send(byte[] data)
    {
        if (!_isRegistered || data == null || data.Length == 0)
            return;
        
        // Build relay data packet: type (1) + sessionId (16) + payload
        var packet = new byte[17 + data.Length];
        packet[0] = (byte)RelayPacketType.Data;
        _sessionId.ToByteArray().CopyTo(packet, 1);
        data.CopyTo(packet, 17);
        
        try
        {
            _udpClient.Send(packet, packet.Length, _relayEndpoint);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Send failed: {ex.Message}");
        }
        
        // Send heartbeat periodically
        SendHeartbeatIfNeeded();
    }
    
    /// <summary>
    /// Sends a disconnect notification and stops the client.
    /// </summary>
    public void Disconnect()
    {
        if (_isRegistered)
        {
            var packet = new byte[17];
            packet[0] = (byte)RelayPacketType.Disconnect;
            _sessionId.ToByteArray().CopyTo(packet, 1);
            
            try
            {
                _udpClient.Send(packet, packet.Length, _relayEndpoint);
            }
            catch { }
        }
        
        Stop();
    }
    
    private void SendRegistration()
    {
        var packet = new byte[17];
        packet[0] = _isHost ? (byte)RelayPacketType.HostRegister : (byte)RelayPacketType.ClientRegister;
        _sessionId.ToByteArray().CopyTo(packet, 1);
        
        try
        {
            _udpClient.Send(packet, packet.Length, _relayEndpoint);
            Console.WriteLine($"[RelayClient] Sent {(_isHost ? "host" : "client")} registration for session {_sessionId}");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Registration failed: {ex.Message}");
        }
    }
    
    private void SendHeartbeatIfNeeded()
    {
        if (DateTime.UtcNow - _lastHeartbeat < HeartbeatInterval)
            return;
        
        var packet = new byte[17];
        packet[0] = (byte)RelayPacketType.Heartbeat;
        _sessionId.ToByteArray().CopyTo(packet, 1);
        
        try
        {
            _udpClient.Send(packet, packet.Length, _relayEndpoint);
            _lastHeartbeat = DateTime.UtcNow;
        }
        catch { }
    }
    
    private async Task ReceiveLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Use Task.Run to make ReceiveAsync cancellable
                var receiveTask = _udpClient.ReceiveAsync();
                var cancellationTask = Task.Delay(-1, _cts.Token);
                var completedTask = await Task.WhenAny(receiveTask, cancellationTask);
                
                if (completedTask == cancellationTask)
                    break;
                
                var result = await receiveTask;
                ProcessPacket(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    OnError?.Invoke($"Socket error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    OnError?.Invoke($"Receive error: {ex.Message}");
                }
            }
        }
    }
    
    private void ProcessPacket(byte[] data)
    {
        if (data.Length < 1)
            return;
        
        var packetType = (RelayPacketType)data[0];
        
        switch (packetType)
        {
            case RelayPacketType.Ack:
                HandleAck(data);
                break;
                
            case RelayPacketType.PeerConnected:
                _peerConnected = true;
                Console.WriteLine($"[RelayClient] Peer connected via relay");
                OnPeerConnected?.Invoke();
                break;
                
            case RelayPacketType.PeerDisconnected:
                _peerConnected = false;
                Console.WriteLine($"[RelayClient] Peer disconnected from relay");
                OnPeerDisconnected?.Invoke();
                break;
                
            default:
                // Data from peer - no relay header, just raw payload
                // The relay server strips the header before forwarding
                OnDataReceived?.Invoke(data);
                break;
        }
    }
    
    private void HandleAck(byte[] data)
    {
        if (data.Length < 18)
            return;
        
        bool success = data[17] == 1;
        string message = data.Length > 18 
            ? System.Text.Encoding.UTF8.GetString(data, 18, data.Length - 18)
            : "";
        
        if (success)
        {
            _isRegistered = true;
            Console.WriteLine($"[RelayClient] Registered with relay: {message}");
            OnRegistered?.Invoke();
        }
        else
        {
            Console.WriteLine($"[RelayClient] Registration failed: {message}");
            OnError?.Invoke($"Registration failed: {message}");
        }
    }
    
    private void Stop()
    {
        _cts.Cancel();
        _isRegistered = false;
        _peerConnected = false;
    }
    
    public void Dispose()
    {
        Disconnect();
        _cts.Dispose();
        _udpClient.Dispose();
    }
}

/// <summary>
/// Types of relay control packets. Must match server-side enum.
/// </summary>
public enum RelayPacketType : byte
{
    HostRegister = 1,
    ClientRegister = 2,
    Data = 3,
    Heartbeat = 4,
    Disconnect = 5,
    Ack = 10,
    PeerConnected = 11,
    PeerDisconnected = 12
}
