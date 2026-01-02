using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace YARG.Introducer;

/// <summary>
/// UDP server that coordinates NAT punch-through between hosts and clients.
/// Works alongside the HTTP introducer to enable P2P connections without port forwarding.
/// </summary>
public sealed class NatPunchServer : IDisposable, INatPunchListener
{
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    
    // Raw UDP socket for diagnostic sends (to bypass LiteNetLib if there are issues)
    private Socket? _rawSocket;
    
    // Track registered hosts waiting for clients
    // Key: lobbyId, Value: host's endpoint info
    private readonly ConcurrentDictionary<string, HostRegistration> _registeredHosts = new();
    
    // Track pending punch requests (client waiting for host to be ready)
    // Key: lobbyId, Value: list of waiting clients
    private readonly ConcurrentDictionary<string, ConcurrentBag<ClientPunchRequest>> _pendingRequests = new();
    
    // Track active punch attempts for timeout
    private readonly ConcurrentDictionary<string, PunchAttempt> _activePunches = new();
    
    // Track client UDP endpoints discovered from actual UDP traffic
    // Key: "lobbyId:clientToken", Value: true external endpoint from UDP
    private readonly ConcurrentDictionary<string, ClientUdpEndpoint> _clientUdpEndpoints = new();
    
    /// <summary>
    /// The UDP port the punch server is listening on.
    /// </summary>
    public int Port { get; }
    
    /// <summary>
    /// Whether the server is running.
    /// </summary>
    public bool IsRunning => _netManager.IsRunning;
    
    /// <summary>
    /// Event fired when a punch attempt succeeds or fails.
    /// </summary>
    public event Action<string, bool, string>? OnPunchResult; // lobbyId, success, message
    
    public NatPunchServer(int port = 9051)
    {
        Port = port;
        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener)
        {
            NatPunchEnabled = true,
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = false,
            IPv6Enabled = false, // Keep it simple for now
        };
        
        // Initialize NAT punch module with this as the listener
        _netManager.NatPunchModule.Init(this);
        
        // Handle unconnected messages (for custom signaling if needed)
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessage;
        
        // Add a low-level network receive event to see ALL incoming traffic
        _listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            Console.WriteLine($"[NatPunchServer] NetworkReceive from peer={peer?.Address}, bytes={reader.AvailableBytes}, channel={channel}");
        };
    }
    
    /// <summary>
    /// Starts the NAT punch server.
    /// </summary>
    public void Start()
    {
        if (_netManager.IsRunning)
        {
            Console.WriteLine($"[NatPunchServer] Already running on port {Port}");
            return;
        }
        
        // CRITICAL: On Fly.io with dedicated IPv4, bind to 0.0.0.0 instead of fly-global-services
        // Testing showed fly-global-services binding receives 0 packets even though outbound works
        // This suggests Fly.io routes inbound UDP to 0.0.0.0 but not to fly-global-services for dedicated IPs
        IPAddress? bindAddress = null;
        var flyAppName = Environment.GetEnvironmentVariable("FLY_APP_NAME");
        
        if (!string.IsNullOrEmpty(flyAppName))
        {
            Console.WriteLine($"[NatPunchServer] Fly.io detected (app={flyAppName})");
            
            // Try to log what fly-global-services resolves to for debugging
            try
            {
                var addresses = System.Net.Dns.GetHostAddresses("fly-global-services");
                var flyGlobalAddr = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                Console.WriteLine($"[NatPunchServer] fly-global-services resolves to: {flyGlobalAddr}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NatPunchServer] Could not resolve fly-global-services: {ex.Message}");
            }
            
            // Bind to 0.0.0.0 - this should receive packets from all interfaces including dedicated IPv4
            bindAddress = null; // Will bind to 0.0.0.0 below
            Console.WriteLine($"[NatPunchServer] Binding to 0.0.0.0 for UDP (dedicated IPv4 routing)");
        }
        
        bool started;
        if (bindAddress != null)
        {
            // Bind to the specific address (fly-global-services)
            started = _netManager.Start(bindAddress, IPAddress.IPv6None, Port);
        }
        else
        {
            // Bind to all interfaces (0.0.0.0)
            started = _netManager.Start(Port);
        }
        
        if (!started)
        {
            throw new InvalidOperationException($"Failed to start NAT punch server on port {Port}");
        }
        
        Console.WriteLine($"[NatPunchServer] Started on UDP port {Port} (bound to {bindAddress?.ToString() ?? "0.0.0.0"})");
        Console.WriteLine($"[NatPunchServer] LocalPort={_netManager.LocalPort}, FirstPeer={_netManager.FirstPeer?.ToString() ?? "null"}");
        
        // Create a raw socket for diagnostic sends (same bind address as LiteNetLib)
        try
        {
            _rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _rawSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            var rawBindEndpoint = new IPEndPoint(bindAddress ?? IPAddress.Any, 0); // Different port to avoid conflict
            _rawSocket.Bind(rawBindEndpoint);
            var localEp = _rawSocket.LocalEndPoint as IPEndPoint;
            Console.WriteLine($"[NatPunchServer] Raw diagnostic socket bound to {localEp}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NatPunchServer] WARNING: Could not create raw socket: {ex.Message}");
            _rawSocket = null;
        }
        
        // Log environment info for debugging UDP issues
        Console.WriteLine($"[NatPunchServer] Environment diagnostics:");
        Console.WriteLine($"[NatPunchServer]   FLY_APP_NAME={Environment.GetEnvironmentVariable("FLY_APP_NAME")}");
        Console.WriteLine($"[NatPunchServer]   FLY_PUBLIC_IP={Environment.GetEnvironmentVariable("FLY_PUBLIC_IP")}");
        Console.WriteLine($"[NatPunchServer]   FLY_ALLOC_ID={Environment.GetEnvironmentVariable("FLY_ALLOC_ID")}");
        
        // Try to determine our actual bound addresses
        try
        {
            var hostName = System.Net.Dns.GetHostName();
            var hostEntry = System.Net.Dns.GetHostEntry(hostName);
            Console.WriteLine($"[NatPunchServer]   Hostname={hostName}");
            Console.WriteLine($"[NatPunchServer]   Host addresses: {string.Join(", ", hostEntry.AddressList.Select(a => $"{a} ({a.AddressFamily})"))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NatPunchServer]   Could not get host addresses: {ex.Message}");
        }
        
        // Start poll loop
        _pollTask = Task.Run(PollLoop);
    }
    
    /// <summary>
    /// Registers a host for NAT punch coordination.
    /// Called when a host registers their lobby with the HTTP introducer.
    /// </summary>
    public void RegisterHost(string lobbyId, IPEndPoint internalEndpoint, IPEndPoint externalEndpoint)
    {
        var registration = new HostRegistration
        {
            LobbyId = lobbyId,
            InternalEndpoint = internalEndpoint,
            ExternalEndpoint = externalEndpoint,
            LastSeen = DateTimeOffset.UtcNow
        };
        
        _registeredHosts[lobbyId] = registration;
        Console.WriteLine($"[NatPunchServer] Host registered for lobby {lobbyId}: internal={internalEndpoint}, external={externalEndpoint}");
        
        // Check if any clients are waiting for this host
        ProcessPendingRequests(lobbyId);
    }
    
    /// <summary>
    /// Updates the host's endpoint (called on heartbeat).
    /// </summary>
    public void UpdateHost(string lobbyId, IPEndPoint? externalEndpoint = null)
    {
        if (_registeredHosts.TryGetValue(lobbyId, out var registration))
        {
            registration.LastSeen = DateTimeOffset.UtcNow;
            if (externalEndpoint != null)
            {
                registration.ExternalEndpoint = externalEndpoint;
            }
        }
    }
    
    /// <summary>
    /// Unregisters a host when they close their lobby.
    /// </summary>
    public void UnregisterHost(string lobbyId)
    {
        if (_registeredHosts.TryRemove(lobbyId, out var registration))
        {
            Console.WriteLine($"[NatPunchServer] Host unregistered for lobby {lobbyId}");
        }
        
        // Clean up any pending requests
        _pendingRequests.TryRemove(lobbyId, out _);
    }
    
    /// <summary>
    /// Initiates a NAT punch request from a client to a host.
    /// Returns immediately - the actual punch happens asynchronously.
    /// </summary>
    public PunchRequestResult RequestPunch(string lobbyId, IPEndPoint clientInternalEndpoint, IPEndPoint clientExternalEndpoint, string clientToken)
    {
        // Check if host is registered
        if (!_registeredHosts.TryGetValue(lobbyId, out var host))
        {
            return new PunchRequestResult(false, "Host not registered for NAT punch", null);
        }
        
        // Check if host registration is stale (more than 60 seconds old)
        if (DateTimeOffset.UtcNow - host.LastSeen > TimeSpan.FromSeconds(60))
        {
            _registeredHosts.TryRemove(lobbyId, out _);
            return new PunchRequestResult(false, "Host registration expired", null);
        }
        
        // CRITICAL: Use the UDP-discovered external endpoint if available
        // The HTTP-reported endpoint may have wrong port due to NAT port mapping
        var udpKey = $"{lobbyId}:{clientToken}";
        IPEndPoint actualClientExternal = clientExternalEndpoint;
        
        if (_clientUdpEndpoints.TryGetValue(udpKey, out var udpEndpoint))
        {
            // Use the endpoint discovered from actual UDP traffic
            actualClientExternal = udpEndpoint.ExternalEndpoint;
            Console.WriteLine($"[NatPunchServer] Using UDP-discovered endpoint: {actualClientExternal} (HTTP reported: {clientExternalEndpoint})");
        }
        else
        {
            Console.WriteLine($"[NatPunchServer] WARNING: No UDP endpoint found for {udpKey}, using HTTP-reported endpoint: {clientExternalEndpoint}");
            Console.WriteLine($"[NatPunchServer] Client should send UDP packets BEFORE HTTP request for reliable NAT punch");
        }
        
        // Generate a unique token for this punch attempt
        var punchToken = $"{lobbyId}:{clientToken}:{Guid.NewGuid():N}";
        
        // Track this punch attempt
        var attempt = new PunchAttempt
        {
            Token = punchToken,
            LobbyId = lobbyId,
            ClientInternal = clientInternalEndpoint,
            ClientExternal = actualClientExternal,
            HostInternal = host.InternalEndpoint,
            HostExternal = host.ExternalEndpoint,
            StartedAt = DateTimeOffset.UtcNow
        };
        _activePunches[punchToken] = attempt;
        
        // Initiate the NAT introduction
        Console.WriteLine($"[NatPunchServer] Initiating punch: client={actualClientExternal} <-> host={host.ExternalEndpoint} (token={punchToken})");
        
        _netManager.NatPunchModule.NatIntroduce(
            hostInternal: host.InternalEndpoint,
            hostExternal: host.ExternalEndpoint,
            clientInternal: clientInternalEndpoint,
            clientExternal: actualClientExternal,
            additionalInfo: punchToken
        );
        
        return new PunchRequestResult(true, "Punch initiated", punchToken);
    }
    
    /// <summary>
    /// Called by LiteNetLib when a NAT introduction request is received.
    /// This happens when a client or host sends SendNatIntroduceRequest().
    /// </summary>
    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        Console.WriteLine($"[NatPunchServer] *** NAT introduction request received ***");
        Console.WriteLine($"[NatPunchServer]   local={localEndPoint}, remote={remoteEndPoint}");
        Console.WriteLine($"[NatPunchServer]   token={token}");
        Console.WriteLine($"[NatPunchServer]   remoteEndPoint.AddressFamily={remoteEndPoint.AddressFamily}");
        
        // Parse the token to determine if this is a host or client registration
        // Token format: "host:{lobbyId}" or "client:{lobbyId}:{clientId}"
        var parts = token.Split(':');
        if (parts.Length < 2)
        {
            Console.WriteLine($"[NatPunchServer] Invalid token format: {token}");
            return;
        }
        
        var role = parts[0];
        var lobbyId = parts[1];
        
        Console.WriteLine($"[NatPunchServer]   role={role}, lobbyId={lobbyId}");
        
        if (role == "host")
        {
            // Host is registering their endpoint via UDP
            // This gives us the TRUE external endpoint from actual UDP traffic
            Console.WriteLine($"[NatPunchServer] Host UDP registration for lobby {lobbyId}: external={remoteEndPoint}");
            RegisterHost(lobbyId, localEndPoint, remoteEndPoint);
        }
        else if (role == "client" && parts.Length >= 3)
        {
            var clientId = parts[2];
            
            // CRITICAL: Store the client's TRUE external endpoint from UDP traffic
            // This is the NAT-assigned endpoint that will actually work for hole punching
            var udpKey = $"{lobbyId}:{clientId}";
            _clientUdpEndpoints[udpKey] = new ClientUdpEndpoint
            {
                LobbyId = lobbyId,
                ClientToken = clientId,
                InternalEndpoint = localEndPoint,
                ExternalEndpoint = remoteEndPoint,
                DiscoveredAt = DateTimeOffset.UtcNow
            };
            Console.WriteLine($"[NatPunchServer] Client UDP endpoint stored: key={udpKey}, external={remoteEndPoint}");
            
            // Client wants to connect to a host
            if (_registeredHosts.TryGetValue(lobbyId, out var host))
            {
                // Host is ready - initiate punch immediately
                var punchToken = $"punch:{lobbyId}:{clientId}:{Guid.NewGuid():N}";
                
                Console.WriteLine($"[NatPunchServer] Client requesting punch to lobby {lobbyId}");
                Console.WriteLine($"[NatPunchServer] NatIntroduce: hostInternal={host.InternalEndpoint}, hostExternal={host.ExternalEndpoint}, clientInternal={localEndPoint}, clientExternal={remoteEndPoint}, token={punchToken}");
                
                _netManager.NatPunchModule.NatIntroduce(
                    hostInternal: host.InternalEndpoint,
                    hostExternal: host.ExternalEndpoint,
                    clientInternal: localEndPoint,
                    clientExternal: remoteEndPoint,
                    additionalInfo: punchToken
                );
                
                // Also send direct unconnected messages to help punch through
                // This ensures we're sending packets even if NatIntroduce has issues
                try
                {
                    var writer = new NetDataWriter();
                    writer.Put((byte)0x50); // 'P' for punch
                    writer.Put(punchToken);
                    writer.Put(host.ExternalEndpoint.ToString());
                    
                    // Send to client via LiteNetLib
                    bool sentToClient = _netManager.SendUnconnectedMessage(writer, remoteEndPoint);
                    Console.WriteLine($"[NatPunchServer] Sent direct punch to client {remoteEndPoint} via LiteNetLib: success={sentToClient}");
                    
                    // Send to host with client info via LiteNetLib
                    var writer2 = new NetDataWriter();
                    writer2.Put((byte)0x50);
                    writer2.Put(punchToken);
                    writer2.Put(remoteEndPoint.ToString());
                    
                    bool sentToHost = _netManager.SendUnconnectedMessage(writer2, host.ExternalEndpoint);
                    Console.WriteLine($"[NatPunchServer] Sent direct punch to host {host.ExternalEndpoint} via LiteNetLib: success={sentToHost}");
                    
                    // Also try raw socket send for diagnostics
                    if (_rawSocket != null)
                    {
                        try
                        {
                            // Send a simple echo packet to client
                            var rawData = System.Text.Encoding.UTF8.GetBytes($"YARG_PUNCH:{punchToken}");
                            int sentBytes = _rawSocket.SendTo(rawData, remoteEndPoint);
                            Console.WriteLine($"[NatPunchServer] Sent RAW UDP to client {remoteEndPoint}: {sentBytes} bytes (from {_rawSocket.LocalEndPoint})");
                            
                            // Send to host too
                            int sentBytesHost = _rawSocket.SendTo(rawData, host.ExternalEndpoint);
                            Console.WriteLine($"[NatPunchServer] Sent RAW UDP to host {host.ExternalEndpoint}: {sentBytesHost} bytes");
                        }
                        catch (Exception rawEx)
                        {
                            Console.WriteLine($"[NatPunchServer] Raw socket send failed: {rawEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NatPunchServer] Error sending direct punch messages: {ex.Message}");
                }
                
                Console.WriteLine($"[NatPunchServer] NatIntroduce called successfully");
            }
            else
            {
                // Host not ready yet - queue the request
                Console.WriteLine($"[NatPunchServer] Host for lobby {lobbyId} not ready, queuing client request");
                
                var request = new ClientPunchRequest
                {
                    ClientId = clientId,
                    InternalEndpoint = localEndPoint,
                    ExternalEndpoint = remoteEndPoint,
                    RequestedAt = DateTimeOffset.UtcNow
                };
                
                var bag = _pendingRequests.GetOrAdd(lobbyId, _ => new ConcurrentBag<ClientPunchRequest>());
                bag.Add(request);
            }
        }
    }
    
    /// <summary>
    /// Called by LiteNetLib when a NAT punch succeeds.
    /// </summary>
    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        Console.WriteLine($"[NatPunchServer] NAT punch success: target={targetEndPoint}, type={type}, token={token}");
        
        // Remove from active punches
        if (_activePunches.TryRemove(token, out var attempt))
        {
            var duration = DateTimeOffset.UtcNow - attempt.StartedAt;
            Console.WriteLine($"[NatPunchServer] Punch completed in {duration.TotalMilliseconds:F0}ms");
            OnPunchResult?.Invoke(attempt.LobbyId, true, $"Punch succeeded to {targetEndPoint}");
        }
    }
    
    private void ProcessPendingRequests(string lobbyId)
    {
        if (!_pendingRequests.TryRemove(lobbyId, out var pendingBag))
            return;
        
        if (!_registeredHosts.TryGetValue(lobbyId, out var host))
            return;
        
        // Process all pending client requests
        while (pendingBag.TryTake(out var request))
        {
            // Skip stale requests (more than 30 seconds old)
            if (DateTimeOffset.UtcNow - request.RequestedAt > TimeSpan.FromSeconds(30))
            {
                Console.WriteLine($"[NatPunchServer] Skipping stale punch request for client {request.ClientId}");
                continue;
            }
            
            var punchToken = $"punch:{lobbyId}:{request.ClientId}:{Guid.NewGuid():N}";
            
            Console.WriteLine($"[NatPunchServer] Processing queued punch request for client {request.ClientId}");
            
            _netManager.NatPunchModule.NatIntroduce(
                hostInternal: host.InternalEndpoint,
                hostExternal: host.ExternalEndpoint,
                clientInternal: request.InternalEndpoint,
                clientExternal: request.ExternalEndpoint,
                additionalInfo: punchToken
            );
        }
    }
    
    private void OnUnconnectedMessage(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Handle any custom signaling messages here if needed
        Console.WriteLine($"[NatPunchServer] Unconnected UDP message from {remoteEndPoint}, type={messageType}, bytes={reader.AvailableBytes}");
    }
    
    private async Task PollLoop()
    {
        Console.WriteLine("[NatPunchServer] Poll loop started");
        var lastStatTime = DateTime.UtcNow;
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _netManager.PollEvents();
                _netManager.NatPunchModule.PollEvents();
                
                // Log UDP stats every 30 seconds for diagnostics
                if ((DateTime.UtcNow - lastStatTime).TotalSeconds >= 30)
                {
                    var stats = _netManager.Statistics;
                    Console.WriteLine($"[NatPunchServer] UDP Stats: packetsReceived={stats.PacketsReceived}, packetsSent={stats.PacketsSent}, bytesReceived={stats.BytesReceived}, bytesSent={stats.BytesSent}");
                    lastStatTime = DateTime.UtcNow;
                }
                
                // Clean up stale punch attempts (older than 30 seconds)
                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
                foreach (var kvp in _activePunches)
                {
                    if (kvp.Value.StartedAt < cutoff)
                    {
                        if (_activePunches.TryRemove(kvp.Key, out var stale))
                        {
                            Console.WriteLine($"[NatPunchServer] Punch attempt timed out: {stale.Token}");
                            OnPunchResult?.Invoke(stale.LobbyId, false, "Punch timed out");
                        }
                    }
                }
                
                // Clean up stale host registrations (older than 90 seconds)
                var hostCutoff = DateTimeOffset.UtcNow.AddSeconds(-90);
                foreach (var kvp in _registeredHosts)
                {
                    if (kvp.Value.LastSeen < hostCutoff)
                    {
                        if (_registeredHosts.TryRemove(kvp.Key, out var stale))
                        {
                            Console.WriteLine($"[NatPunchServer] Host registration expired: {stale.LobbyId}");
                        }
                    }
                }
                
                // Clean up stale UDP endpoints (older than 60 seconds)
                var udpCutoff = DateTimeOffset.UtcNow.AddSeconds(-60);
                foreach (var kvp in _clientUdpEndpoints)
                {
                    if (kvp.Value.DiscoveredAt < udpCutoff)
                    {
                        _clientUdpEndpoints.TryRemove(kvp.Key, out _);
                    }
                }
                
                await Task.Delay(15, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NatPunchServer] Poll error: {ex.Message}");
            }
        }
        
        Console.WriteLine("[NatPunchServer] Poll loop ended");
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _pollTask?.Wait(TimeSpan.FromSeconds(2));
        _netManager.Stop();
        _rawSocket?.Close();
        _rawSocket?.Dispose();
        _cts.Dispose();
        Console.WriteLine("[NatPunchServer] Disposed");
    }
    
    // Helper classes
    
    private class HostRegistration
    {
        public required string LobbyId { get; init; }
        public required IPEndPoint InternalEndpoint { get; set; }
        public required IPEndPoint ExternalEndpoint { get; set; }
        public DateTimeOffset LastSeen { get; set; }
    }
    
    private class ClientPunchRequest
    {
        public required string ClientId { get; init; }
        public required IPEndPoint InternalEndpoint { get; init; }
        public required IPEndPoint ExternalEndpoint { get; init; }
        public DateTimeOffset RequestedAt { get; init; }
    }
    
    private class ClientUdpEndpoint
    {
        public required string LobbyId { get; init; }
        public required string ClientToken { get; init; }
        public required IPEndPoint InternalEndpoint { get; init; }
        public required IPEndPoint ExternalEndpoint { get; init; }
        public DateTimeOffset DiscoveredAt { get; init; }
    }
    
    private class PunchAttempt
    {
        public required string Token { get; init; }
        public required string LobbyId { get; init; }
        public required IPEndPoint ClientInternal { get; init; }
        public required IPEndPoint ClientExternal { get; init; }
        public required IPEndPoint HostInternal { get; init; }
        public required IPEndPoint HostExternal { get; init; }
        public DateTimeOffset StartedAt { get; init; }
    }
}

/// <summary>
/// Result of a punch request.
/// </summary>
public record PunchRequestResult(bool Success, string Message, string? PunchToken);
