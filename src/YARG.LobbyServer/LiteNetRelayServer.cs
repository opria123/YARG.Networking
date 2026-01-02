using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace YARG.LobbyServer;

/// <summary>
/// LiteNetLib-based relay server that forwards packets between hosts and clients.
/// Both host and client connect to this relay using LiteNetLib connections.
/// The relay then bridges packets between them transparently.
/// </summary>
public sealed class LiteNetRelayServer : INetEventListener, IDisposable
{
    private readonly NetManager _server;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    
    // Relay sessions: SessionId -> Session
    private readonly ConcurrentDictionary<Guid, LiteNetRelaySession> _sessions = new();
    
    // Peer to session mapping
    private readonly ConcurrentDictionary<int, (Guid sessionId, bool isHost)> _peerToSession = new();
    
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
    
    // Relay packet opcodes (first byte of data)
    private const byte OPCODE_REGISTER = 1;
    private const byte OPCODE_DATA = 2;
    private const byte OPCODE_REGISTERED = 10;
    private const byte OPCODE_PEER_CONNECTED = 11;
    private const byte OPCODE_PEER_DISCONNECTED = 12;
    private const byte OPCODE_ERROR = 20;
    
    public LiteNetRelayServer(int port)
    {
        _port = port;
        _server = new NetManager(this)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            DisconnectTimeout = 30000, // 30 seconds
            UpdateTime = 15,
            UnsyncedEvents = false, // We'll poll manually
            BroadcastReceiveEnabled = false,
            IPv6Enabled = false,
        };
    }
    
    public void Start()
    {
        if (IsRunning) return;
        
        if (!_server.Start(_port))
        {
            Console.WriteLine($"[LiteNetRelayServer] Failed to start on port {_port}");
            return;
        }
        
        IsRunning = true;
        _pollTask = Task.Run(PollLoopAsync);
        Console.WriteLine($"[LiteNetRelayServer] Started on UDP port {_port}");
    }
    
    /// <summary>
    /// Allocates a new relay session for a lobby.
    /// Returns the session ID that both host and client should use.
    /// </summary>
    public RelayAllocationResult AllocateSession(Guid lobbyId, string? hostIdentifier = null)
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
        var session = new LiteNetRelaySession
        {
            SessionId = sessionId,
            LobbyId = lobbyId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };
        
        _sessions[sessionId] = session;
        Interlocked.Increment(ref _sessionsCreated);
        
        Console.WriteLine($"[LiteNetRelayServer] Allocated session {sessionId} for lobby {lobbyId}");
        
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
            // Clean up peer mappings
            if (session.HostPeer != null)
            {
                _peerToSession.TryRemove(session.HostPeer.Id, out _);
                session.HostPeer.Disconnect();
            }
            if (session.ClientPeer != null)
            {
                _peerToSession.TryRemove(session.ClientPeer.Id, out _);
                session.ClientPeer.Disconnect();
            }
            
            Console.WriteLine($"[LiteNetRelayServer] Released session {sessionId} (relayed {session.PacketsRelayed} packets, {session.BytesRelayed} bytes)");
        }
    }
    
    private async Task PollLoopAsync()
    {
        Console.WriteLine("[LiteNetRelayServer] Poll loop started");
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _server.PollEvents();
                
                // Periodic cleanup
                if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
                {
                    CleanupStaleSessions();
                    _lastCleanup = DateTime.UtcNow;
                }
                
                await Task.Delay(15, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiteNetRelayServer] Poll error: {ex.Message}");
            }
        }
        
        Console.WriteLine("[LiteNetRelayServer] Poll loop ended");
    }
    
    // INetEventListener implementation
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Accept all connections - authentication happens via session registration
        Console.WriteLine($"[LiteNetRelayServer] Connection request from {request.RemoteEndPoint}");
        request.Accept();
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"[LiteNetRelayServer] Peer connected: {peer.Id} from {peer.Address}:{peer.Port}");
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"[LiteNetRelayServer] Peer disconnected: {peer.Id}, reason: {disconnectInfo.Reason}");
        
        if (_peerToSession.TryRemove(peer.Id, out var mapping))
        {
            if (_sessions.TryGetValue(mapping.sessionId, out var session))
            {
                if (mapping.isHost)
                {
                    session.HostPeer = null;
                    // Notify client that host disconnected
                    if (session.ClientPeer != null)
                    {
                        SendPeerDisconnected(session.ClientPeer, mapping.sessionId);
                    }
                }
                else
                {
                    session.ClientPeer = null;
                    // Notify host that client disconnected
                    if (session.HostPeer != null)
                    {
                        SendPeerDisconnected(session.HostPeer, mapping.sessionId);
                    }
                }
                
                // If both peers are gone, remove the session
                if (session.HostPeer == null && session.ClientPeer == null)
                {
                    _sessions.TryRemove(mapping.sessionId, out _);
                    Console.WriteLine($"[LiteNetRelayServer] Session {mapping.sessionId} ended (both peers disconnected)");
                }
            }
        }
    }
    
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Console.WriteLine($"[LiteNetRelayServer] Network error from {endPoint}: {socketError}");
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
            case OPCODE_REGISTER:
                HandleRegister(peer, reader);
                break;
                
            case OPCODE_DATA:
                HandleData(peer, reader, channelNumber, deliveryMethod);
                break;
                
            default:
                Console.WriteLine($"[LiteNetRelayServer] Unknown opcode {opcode} from peer {peer.Id}");
                break;
        }
        
        reader.Recycle();
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Not used
        reader.Recycle();
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Not used
    }
    
    private void HandleRegister(NetPeer peer, NetPacketReader reader)
    {
        if (reader.AvailableBytes < 17) // 16 bytes session ID + 1 byte isHost
        {
            SendError(peer, Guid.Empty, "Invalid registration packet");
            return;
        }
        
        var sessionIdBytes = new byte[16];
        reader.GetBytes(sessionIdBytes, 16);
        var sessionId = new Guid(sessionIdBytes);
        var isHost = reader.GetBool();
        
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            Console.WriteLine($"[LiteNetRelayServer] Registration for unknown session {sessionId}");
            SendError(peer, sessionId, "Session not found. Request a new relay allocation.");
            return;
        }
        
        if (isHost)
        {
            if (session.HostPeer != null && session.HostPeer.Id != peer.Id)
            {
                Console.WriteLine($"[LiteNetRelayServer] Session {sessionId} already has a host");
                SendError(peer, sessionId, "Session already has a host");
                return;
            }
            
            session.HostPeer = peer;
            _peerToSession[peer.Id] = (sessionId, true);
            Console.WriteLine($"[LiteNetRelayServer] Host registered for session {sessionId}: peer {peer.Id}");
        }
        else
        {
            if (session.ClientPeer != null && session.ClientPeer.Id != peer.Id)
            {
                Console.WriteLine($"[LiteNetRelayServer] Session {sessionId} already has a client");
                SendError(peer, sessionId, "Session already has a client");
                return;
            }
            
            session.ClientPeer = peer;
            _peerToSession[peer.Id] = (sessionId, false);
            Console.WriteLine($"[LiteNetRelayServer] Client registered for session {sessionId}: peer {peer.Id}");
        }
        
        session.LastActivity = DateTime.UtcNow;
        
        // Send registration confirmation
        SendRegistered(peer, sessionId, isHost);
        
        // If both peers are now connected, notify them
        if (session.HostPeer != null && session.ClientPeer != null)
        {
            Console.WriteLine($"[LiteNetRelayServer] Session {sessionId} fully connected!");
            SendPeerConnected(session.HostPeer, sessionId);
            SendPeerConnected(session.ClientPeer, sessionId);
        }
    }
    
    private void HandleData(NetPeer sender, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!_peerToSession.TryGetValue(sender.Id, out var mapping))
        {
            Console.WriteLine($"[LiteNetRelayServer] Data from unregistered peer {sender.Id}");
            return;
        }
        
        if (!_sessions.TryGetValue(mapping.sessionId, out var session))
        {
            return;
        }
        
        session.LastActivity = DateTime.UtcNow;
        
        // Get the destination peer
        NetPeer? destination = mapping.isHost ? session.ClientPeer : session.HostPeer;
        
        if (destination == null)
        {
            // Other peer not connected yet, drop the packet
            return;
        }
        
        // Get the actual payload (everything after the opcode)
        var payloadLength = reader.AvailableBytes;
        if (payloadLength == 0)
        {
            return;
        }
        
        var payload = new byte[payloadLength];
        reader.GetBytes(payload, payloadLength);
        
        // Forward the payload to the other peer
        // We wrap it in our relay data format
        var writer = new NetDataWriter();
        writer.Put(OPCODE_DATA);
        writer.Put(payload);
        
        destination.Send(writer, deliveryMethod);
        
        session.PacketsRelayed++;
        session.BytesRelayed += payloadLength;
        Interlocked.Increment(ref _packetsRelayed);
        Interlocked.Add(ref _bytesRelayed, payloadLength);
    }
    
    private void SendRegistered(NetPeer peer, Guid sessionId, bool isHost)
    {
        var writer = new NetDataWriter();
        writer.Put(OPCODE_REGISTERED);
        writer.Put(sessionId.ToByteArray());
        writer.Put(isHost);
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
    
    private void SendPeerConnected(NetPeer peer, Guid sessionId)
    {
        var writer = new NetDataWriter();
        writer.Put(OPCODE_PEER_CONNECTED);
        writer.Put(sessionId.ToByteArray());
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
    
    private void SendPeerDisconnected(NetPeer peer, Guid sessionId)
    {
        var writer = new NetDataWriter();
        writer.Put(OPCODE_PEER_DISCONNECTED);
        writer.Put(sessionId.ToByteArray());
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
    
    private void SendError(NetPeer peer, Guid sessionId, string message)
    {
        var writer = new NetDataWriter();
        writer.Put(OPCODE_ERROR);
        writer.Put(sessionId.ToByteArray());
        writer.Put(message);
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
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
            Console.WriteLine($"[LiteNetRelayServer] Cleaned up stale session {sessionId}");
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
        _pollTask?.Wait(TimeSpan.FromSeconds(2));
        _server.Stop();
        _cts.Dispose();
        IsRunning = false;
        Console.WriteLine("[LiteNetRelayServer] Disposed");
    }
}

/// <summary>
/// Represents an active LiteNetLib relay session between a host and client.
/// </summary>
public class LiteNetRelaySession
{
    public Guid SessionId { get; init; }
    public Guid LobbyId { get; init; }
    public NetPeer? HostPeer { get; set; }
    public NetPeer? ClientPeer { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivity { get; set; }
    public long PacketsRelayed { get; set; }
    public long BytesRelayed { get; set; }
}
