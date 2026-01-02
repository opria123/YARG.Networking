using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace YARG.Introducer;

/// <summary>
/// UDP relay server that forwards packets between hosts and clients.
/// Used when direct P2P connections are not possible (strict NAT, firewalls, etc.)
/// </summary>
public sealed class RelayServer : IDisposable
{
    private readonly UdpClient _udpServer;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    
    // Relay sessions: SessionId -> Session
    private readonly ConcurrentDictionary<Guid, RelaySession> _sessions = new();
    
    // Endpoint to session mapping for routing incoming packets
    private readonly ConcurrentDictionary<string, RelaySession> _endpointToSession = new();
    
    // Stats
    private long _packetsRelayed;
    private long _bytesRelayed;
    private long _sessionsCreated;
    
    public int Port => _port;
    public bool IsRunning { get; private set; }
    public long PacketsRelayed => _packetsRelayed;
    public long BytesRelayed => _bytesRelayed;
    public int ActiveSessions => _sessions.Count;
    
    // Maximum session age before cleanup
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private DateTime _lastCleanup = DateTime.UtcNow;
    
    public RelayServer(int port)
    {
        _port = port;
        _udpServer = new UdpClient(port);
        
        // Increase buffer sizes for better performance
        _udpServer.Client.ReceiveBufferSize = 1024 * 1024; // 1MB
        _udpServer.Client.SendBufferSize = 1024 * 1024;
    }
    
    public void Start()
    {
        if (IsRunning) return;
        
        IsRunning = true;
        _receiveTask = Task.Run(ReceiveLoopAsync);
        Console.WriteLine($"[RelayServer] Started on UDP port {_port}");
    }
    
    /// <summary>
    /// Allocates a new relay session for a lobby.
    /// Returns the session ID that both host and client should use.
    /// </summary>
    public RelayAllocationResult AllocateSession(Guid lobbyId, string hostEndpoint)
    {
        // Check if session already exists for this lobby
        var existingSession = _sessions.Values.FirstOrDefault(s => s.LobbyId == lobbyId);
        if (existingSession != null)
        {
            return new RelayAllocationResult
            {
                Success = true,
                SessionId = existingSession.SessionId,
                RelayPort = _port,
                Message = "Existing session reused"
            };
        }
        
        var sessionId = Guid.NewGuid();
        var session = new RelaySession
        {
            SessionId = sessionId,
            LobbyId = lobbyId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };
        
        _sessions[sessionId] = session;
        Interlocked.Increment(ref _sessionsCreated);
        
        Console.WriteLine($"[RelayServer] Allocated session {sessionId} for lobby {lobbyId}");
        
        return new RelayAllocationResult
        {
            Success = true,
            SessionId = sessionId,
            RelayPort = _port,
            Message = "Session allocated"
        };
    }
    
    /// <summary>
    /// Releases a relay session.
    /// </summary>
    public void ReleaseSession(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            // Clean up endpoint mappings
            if (session.HostEndpoint != null)
            {
                _endpointToSession.TryRemove(session.HostEndpoint.ToString(), out _);
            }
            if (session.ClientEndpoint != null)
            {
                _endpointToSession.TryRemove(session.ClientEndpoint.ToString(), out _);
            }
            
            Console.WriteLine($"[RelayServer] Released session {sessionId} (relayed {session.PacketsRelayed} packets, {session.BytesRelayed} bytes)");
        }
    }
    
    private async Task ReceiveLoopAsync()
    {
        Console.WriteLine("[RelayServer] Receive loop started");
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpServer.ReceiveAsync(_cts.Token);
                ProcessPacket(result.RemoteEndPoint, result.Buffer);
                
                // Periodic cleanup
                if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
                {
                    CleanupStaleSessions();
                    _lastCleanup = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[RelayServer] Socket error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RelayServer] Error: {ex.Message}");
            }
        }
        
        Console.WriteLine("[RelayServer] Receive loop ended");
    }
    
    private void ProcessPacket(IPEndPoint sender, byte[] data)
    {
        if (data.Length < 17) // Minimum: 1 byte type + 16 byte session GUID
        {
            return;
        }
        
        // First byte is packet type
        var packetType = (RelayPacketType)data[0];
        
        // Next 16 bytes are session ID
        var sessionIdBytes = new byte[16];
        Array.Copy(data, 1, sessionIdBytes, 0, 16);
        var sessionId = new Guid(sessionIdBytes);
        
        // Remaining bytes are payload
        var payload = new byte[data.Length - 17];
        if (payload.Length > 0)
        {
            Array.Copy(data, 17, payload, 0, payload.Length);
        }
        
        switch (packetType)
        {
            case RelayPacketType.HostRegister:
                HandleHostRegister(sessionId, sender);
                break;
                
            case RelayPacketType.ClientRegister:
                HandleClientRegister(sessionId, sender);
                break;
                
            case RelayPacketType.Data:
                HandleDataPacket(sessionId, sender, payload);
                break;
                
            case RelayPacketType.Heartbeat:
                HandleHeartbeat(sessionId, sender);
                break;
                
            case RelayPacketType.Disconnect:
                HandleDisconnect(sessionId, sender);
                break;
        }
    }
    
    private void HandleHostRegister(Guid sessionId, IPEndPoint sender)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            Console.WriteLine($"[RelayServer] Host register for unknown session {sessionId}");
            SendAck(sender, sessionId, false, "Session not found");
            return;
        }
        
        session.HostEndpoint = sender;
        session.LastActivity = DateTime.UtcNow;
        _endpointToSession[sender.ToString()] = session;
        
        Console.WriteLine($"[RelayServer] Host registered for session {sessionId}: {sender}");
        SendAck(sender, sessionId, true, "Host registered");
        
        // If client is already connected, notify both
        if (session.ClientEndpoint != null)
        {
            NotifyPeerConnected(session);
        }
    }
    
    private void HandleClientRegister(Guid sessionId, IPEndPoint sender)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            Console.WriteLine($"[RelayServer] Client register for unknown session {sessionId}");
            SendAck(sender, sessionId, false, "Session not found");
            return;
        }
        
        session.ClientEndpoint = sender;
        session.LastActivity = DateTime.UtcNow;
        _endpointToSession[sender.ToString()] = session;
        
        Console.WriteLine($"[RelayServer] Client registered for session {sessionId}: {sender}");
        SendAck(sender, sessionId, true, "Client registered");
        
        // If host is already connected, notify both
        if (session.HostEndpoint != null)
        {
            NotifyPeerConnected(session);
        }
    }
    
    private void HandleDataPacket(Guid sessionId, IPEndPoint sender, byte[] payload)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }
        
        session.LastActivity = DateTime.UtcNow;
        
        // Determine destination based on sender
        IPEndPoint? destination = null;
        
        if (session.HostEndpoint != null && sender.Equals(session.HostEndpoint))
        {
            // Packet from host -> forward to client
            destination = session.ClientEndpoint;
        }
        else if (session.ClientEndpoint != null && sender.Equals(session.ClientEndpoint))
        {
            // Packet from client -> forward to host
            destination = session.HostEndpoint;
        }
        
        if (destination == null)
        {
            return;
        }
        
        // Forward the payload (without the relay header)
        try
        {
            _udpServer.Send(payload, payload.Length, destination);
            
            session.PacketsRelayed++;
            session.BytesRelayed += payload.Length;
            Interlocked.Increment(ref _packetsRelayed);
            Interlocked.Add(ref _bytesRelayed, payload.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RelayServer] Failed to forward packet: {ex.Message}");
        }
    }
    
    private void HandleHeartbeat(Guid sessionId, IPEndPoint sender)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastActivity = DateTime.UtcNow;
        }
    }
    
    private void HandleDisconnect(Guid sessionId, IPEndPoint sender)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }
        
        // Notify the other peer
        IPEndPoint? otherPeer = null;
        if (session.HostEndpoint != null && sender.Equals(session.HostEndpoint))
        {
            otherPeer = session.ClientEndpoint;
            session.HostEndpoint = null;
        }
        else if (session.ClientEndpoint != null && sender.Equals(session.ClientEndpoint))
        {
            otherPeer = session.HostEndpoint;
            session.ClientEndpoint = null;
        }
        
        _endpointToSession.TryRemove(sender.ToString(), out _);
        
        if (otherPeer != null)
        {
            SendDisconnectNotification(otherPeer, sessionId);
        }
        
        // If both peers disconnected, release the session
        if (session.HostEndpoint == null && session.ClientEndpoint == null)
        {
            ReleaseSession(sessionId);
        }
        
        Console.WriteLine($"[RelayServer] Peer {sender} disconnected from session {sessionId}");
    }
    
    private void SendAck(IPEndPoint destination, Guid sessionId, bool success, string message)
    {
        try
        {
            // ACK packet: type (1) + sessionId (16) + success (1) + message
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
            var packet = new byte[18 + messageBytes.Length];
            packet[0] = (byte)RelayPacketType.Ack;
            sessionId.ToByteArray().CopyTo(packet, 1);
            packet[17] = success ? (byte)1 : (byte)0;
            messageBytes.CopyTo(packet, 18);
            
            _udpServer.Send(packet, packet.Length, destination);
        }
        catch { }
    }
    
    private void NotifyPeerConnected(RelaySession session)
    {
        // Notify both peers that the relay is ready
        var packet = new byte[17];
        packet[0] = (byte)RelayPacketType.PeerConnected;
        session.SessionId.ToByteArray().CopyTo(packet, 1);
        
        try
        {
            if (session.HostEndpoint != null)
            {
                _udpServer.Send(packet, packet.Length, session.HostEndpoint);
            }
            if (session.ClientEndpoint != null)
            {
                _udpServer.Send(packet, packet.Length, session.ClientEndpoint);
            }
            
            Console.WriteLine($"[RelayServer] Session {session.SessionId} fully connected (host: {session.HostEndpoint}, client: {session.ClientEndpoint})");
        }
        catch { }
    }
    
    private void SendDisconnectNotification(IPEndPoint destination, Guid sessionId)
    {
        try
        {
            var packet = new byte[17];
            packet[0] = (byte)RelayPacketType.PeerDisconnected;
            sessionId.ToByteArray().CopyTo(packet, 1);
            _udpServer.Send(packet, packet.Length, destination);
        }
        catch { }
    }
    
    private void CleanupStaleSessions()
    {
        var cutoff = DateTime.UtcNow - SessionTimeout;
        var staleSessions = _sessions
            .Where(kvp => kvp.Value.LastActivity < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var sessionId in staleSessions)
        {
            ReleaseSession(sessionId);
            Console.WriteLine($"[RelayServer] Cleaned up stale session {sessionId}");
        }
    }
    
    public RelayStats GetStats()
    {
        return new RelayStats
        {
            IsRunning = IsRunning,
            Port = _port,
            ActiveSessions = _sessions.Count,
            TotalSessionsCreated = _sessionsCreated,
            PacketsRelayed = _packetsRelayed,
            BytesRelayed = _bytesRelayed
        };
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _receiveTask?.Wait(TimeSpan.FromSeconds(2));
        _udpServer.Dispose();
        _cts.Dispose();
        IsRunning = false;
        Console.WriteLine("[RelayServer] Disposed");
    }
}

/// <summary>
/// Represents an active relay session between a host and client.
/// </summary>
public class RelaySession
{
    public Guid SessionId { get; init; }
    public Guid LobbyId { get; init; }
    public IPEndPoint? HostEndpoint { get; set; }
    public IPEndPoint? ClientEndpoint { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivity { get; set; }
    public long PacketsRelayed { get; set; }
    public long BytesRelayed { get; set; }
}

/// <summary>
/// Result of allocating a relay session.
/// </summary>
public class RelayAllocationResult
{
    public bool Success { get; init; }
    public Guid SessionId { get; init; }
    public int RelayPort { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Relay server statistics.
/// </summary>
public class RelayStats
{
    public bool IsRunning { get; init; }
    public int Port { get; init; }
    public int ActiveSessions { get; init; }
    public long TotalSessionsCreated { get; init; }
    public long PacketsRelayed { get; init; }
    public long BytesRelayed { get; init; }
}

/// <summary>
/// Types of relay control packets.
/// </summary>
public enum RelayPacketType : byte
{
    /// <summary>Host registering with the relay</summary>
    HostRegister = 1,
    
    /// <summary>Client registering with the relay</summary>
    ClientRegister = 2,
    
    /// <summary>Data packet to forward</summary>
    Data = 3,
    
    /// <summary>Keepalive heartbeat</summary>
    Heartbeat = 4,
    
    /// <summary>Peer disconnecting</summary>
    Disconnect = 5,
    
    /// <summary>Acknowledgment from server</summary>
    Ack = 10,
    
    /// <summary>Other peer connected notification</summary>
    PeerConnected = 11,
    
    /// <summary>Other peer disconnected notification</summary>
    PeerDisconnected = 12
}
