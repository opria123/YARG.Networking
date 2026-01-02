using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YARG.Net.Directory;

namespace YARG.LobbyServer;

internal static class Program
{
    private static readonly ConcurrentDictionary<Guid, LobbyRecord> Lobbies = new();
    private static readonly ConcurrentDictionary<string, Guid> LobbyCodes = new(); // code -> lobbyId
    private static readonly ConcurrentDictionary<Guid, string> LobbyIdToCodes = new(); // lobbyId -> code
    
    // NAT punch server for coordinating hole punching
    private static NatPunchServer? _punchServer;
    
    // Relay server for when direct connections fail (LiteNetLib-based)
    private static LiteNetRelayServer? _relayServer;
    
    // Default UDP port for NAT punch coordination
    private const int DefaultPunchPort = 9051;
    
    // Default UDP port for relay
    private const int DefaultRelayPort = 9052;

    // Lobbies are considered stale if not updated within this time
    private static readonly TimeSpan LobbyTtl = TimeSpan.FromSeconds(30);

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add CORS for local development
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();
        app.UseCors();
        
        // Initialize NAT punch server
        var punchPort = int.TryParse(Environment.GetEnvironmentVariable("PUNCH_PORT"), out var p) ? p : DefaultPunchPort;
        try
        {
            _punchServer = new NatPunchServer(punchPort);
            _punchServer.Start();
            Console.WriteLine($"[LobbyServer] NAT punch server started on UDP port {punchPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LobbyServer] WARNING: Failed to start NAT punch server: {ex.Message}");
            Console.WriteLine("[LobbyServer] NAT punch-through will not be available");
        }
        
        // Initialize Relay server (LiteNetLib-based)
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("RELAY_PORT"), out var r) ? r : DefaultRelayPort;
        try
        {
            _relayServer = new LiteNetRelayServer(relayPort);
            _relayServer.Start();
            Console.WriteLine($"[LobbyServer] LiteNetLib Relay server started on UDP port {relayPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LobbyServer] WARNING: Failed to start Relay server: {ex.Message}");
            Console.WriteLine("[LobbyServer] Relay connections will not be available");
        }
        
        // Graceful shutdown
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            Console.WriteLine("[LobbyServer] Shutting down servers...");
            _punchServer?.Dispose();
            _relayServer?.Dispose();
        });

        // Health check - enhanced with punch and relay server status
        app.MapGet("/health", () => Results.Ok(new 
        { 
            status = "healthy", 
            timestamp = DateTimeOffset.UtcNow,
            punchServerRunning = _punchServer?.IsRunning ?? false,
            punchServerPort = _punchServer?.Port ?? 0,
            relayServerRunning = _relayServer?.IsRunning ?? false,
            relayServerPort = _relayServer?.Port ?? 0,
            relayActiveSessions = _relayServer?.ActiveSessions ?? 0
        }));

        // GET /api/lobbies - List all active lobbies
        app.MapGet("/api/lobbies", () =>
        {
            PurgeStaleLobbies();
            
            var entries = Lobbies.Values
                .Where(r => r.IsActive)
                .Select(r => r.ToDirectoryEntry())
                .ToList();
                
            return Results.Ok(entries);
        });

        // POST /api/lobbies - Register/heartbeat a lobby
        app.MapPost("/api/lobbies", (HttpContext context, LobbyAdvertisementRequest request) =>
        {
            if (request.LobbyId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "LobbyId is required" });
            }

            // Try to determine the public IP from the request if not provided
            string address = request.Address;
            if (string.IsNullOrWhiteSpace(address) || address == "0.0.0.0")
            {
                address = GetClientIpAddress(context);
            }

            var record = Lobbies.AddOrUpdate(
                request.LobbyId,
                _ => new LobbyRecord
                {
                    LobbyId = request.LobbyId,
                    LobbyName = request.LobbyName,
                    HostName = request.HostName,
                    Address = address,
                    Port = request.Port,
                    CurrentPlayers = request.CurrentPlayers,
                    MaxPlayers = request.MaxPlayers,
                    HasPassword = request.HasPassword,
                    Version = request.Version,
                    LastSeen = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                (_, existing) =>
                {
                    existing.LobbyName = request.LobbyName;
                    existing.HostName = request.HostName;
                    existing.Address = address;
                    existing.Port = request.Port;
                    existing.CurrentPlayers = request.CurrentPlayers;
                    existing.MaxPlayers = request.MaxPlayers;
                    existing.HasPassword = request.HasPassword;
                    existing.Version = request.Version;
                    existing.LastSeen = DateTimeOffset.UtcNow;
                    return existing;
                });

            Console.WriteLine($"[LobbyServer] Lobby '{request.LobbyName}' ({request.LobbyId}) registered/updated from {address}:{request.Port}");
            
            return Results.Ok(record.ToDirectoryEntry());
        });

        // DELETE /api/lobbies/{id} - Remove a lobby
        app.MapDelete("/api/lobbies/{id:guid}", (Guid id) =>
        {
            if (Lobbies.TryRemove(id, out var removed))
            {
                // Also clean up any associated code
                CleanupLobbyCode(id);
                Console.WriteLine($"[LobbyServer] Lobby '{removed.LobbyName}' ({id}) removed");
                return Results.Ok(new { removed = true });
            }
            
            return Results.NotFound(new { error = "Lobby not found" });
        });

        // ========== LOBBY CODE ENDPOINTS ==========
        
        // POST /api/lobbies/code - Generate a code for a lobby
        app.MapPost("/api/lobbies/code", (LobbyCodeRequest request) =>
        {
            if (request.LobbyId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "LobbyId is required" });
            }

            // Check if lobby exists
            if (!Lobbies.TryGetValue(request.LobbyId, out var lobby))
            {
                return Results.NotFound(new { error = "Lobby not found. Register the lobby first." });
            }

            // Check if this lobby already has a code
            if (LobbyIdToCodes.TryGetValue(request.LobbyId, out var existingCode))
            {
                Console.WriteLine($"[LobbyServer] Lobby '{lobby.LobbyName}' already has code: {existingCode}");
                return Results.Ok(new LobbyCodeResponse(existingCode, request.LobbyId));
            }

            // Generate a new unique code
            var code = GenerateUniqueCode();
            
            // Store bidirectional mapping
            LobbyCodes[code] = request.LobbyId;
            LobbyIdToCodes[request.LobbyId] = code;

            Console.WriteLine($"[LobbyServer] Generated code '{code}' for lobby '{lobby.LobbyName}' ({request.LobbyId})");
            
            return Results.Ok(new LobbyCodeResponse(code, request.LobbyId));
        });

        // GET /api/lobbies/code/{code} - Look up a lobby by code
        app.MapGet("/api/lobbies/code/{code}", (string code) =>
        {
            // Normalize to uppercase
            code = code.ToUpperInvariant();
            
            if (code.Length != 6)
            {
                return Results.BadRequest(new { error = "Code must be 6 characters" });
            }

            if (!LobbyCodes.TryGetValue(code, out var lobbyId))
            {
                return Results.NotFound(new { error = "Invalid or expired code" });
            }

            if (!Lobbies.TryGetValue(lobbyId, out var lobby))
            {
                // Lobby was removed but code wasn't cleaned up - fix it now
                CleanupLobbyCode(lobbyId);
                return Results.NotFound(new { error = "Lobby no longer exists" });
            }

            if (!lobby.IsActive)
            {
                return Results.NotFound(new { error = "Lobby has expired" });
            }

            Console.WriteLine($"[LobbyServer] Code '{code}' resolved to lobby '{lobby.LobbyName}' ({lobbyId})");
            
            return Results.Ok(lobby.ToDirectoryEntry());
        });

        // DELETE /api/lobbies/code/{code} - Release a code
        app.MapDelete("/api/lobbies/code/{code}", (string code) =>
        {
            code = code.ToUpperInvariant();
            
            if (LobbyCodes.TryRemove(code, out var lobbyId))
            {
                LobbyIdToCodes.TryRemove(lobbyId, out _);
                Console.WriteLine($"[LobbyServer] Code '{code}' released for lobby {lobbyId}");
                return Results.Ok(new { released = true });
            }
            
            return Results.NotFound(new { error = "Code not found" });
        });

        // ========== NAT PUNCH ENDPOINTS ==========
        
        // GET /api/punch/info - Get NAT punch server info
        app.MapGet("/api/punch/info", (HttpContext context) =>
        {
            if (_punchServer == null || !_punchServer.IsRunning)
            {
                return Results.Ok(new PunchInfoResponse(
                    Available: false,
                    Address: null,
                    Port: 0,
                    Message: "NAT punch server not available"
                ));
            }
            
            // Return the server's public address for clients to connect to
            // In production, this should be the public IP/hostname of the lobby server
            var serverHost = context.Request.Host.Host;
            if (serverHost == "localhost" || serverHost == "127.0.0.1")
            {
                // Try to get actual LAN IP for local testing
                serverHost = GetServerPublicAddress() ?? serverHost;
            }
            
            return Results.Ok(new PunchInfoResponse(
                Available: true,
                Address: serverHost,
                Port: _punchServer.Port,
                Message: "NAT punch server ready"
            ));
        });
        
        // POST /api/punch/register - Register a host for NAT punch coordination
        app.MapPost("/api/punch/register", IResult (HttpContext context, PunchRegisterRequest request) =>
        {
            if (_punchServer == null || !_punchServer.IsRunning)
            {
                return Results.StatusCode(503); // Service Unavailable
            }
            
            if (request.LobbyId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "LobbyId is required" });
            }
            
            // Get client's external IP from request
            var externalIp = GetClientIpAddress(context);
            var externalEndpoint = new IPEndPoint(IPAddress.Parse(externalIp), request.ExternalPort);
            
            // Parse internal endpoint
            if (!IPEndPoint.TryParse(request.InternalEndpoint, out var internalEndpoint))
            {
                return Results.BadRequest(new { error = "Invalid internal endpoint format" });
            }
            
            var lobbyIdStr = request.LobbyId.ToString();
            _punchServer.RegisterHost(lobbyIdStr, internalEndpoint, externalEndpoint);
            
            Console.WriteLine($"[LobbyServer] Host registered for punch: lobby={lobbyIdStr}, internal={internalEndpoint}, external={externalEndpoint}");
            
            return Results.Ok(new { registered = true, lobbyId = request.LobbyId });
        });
        
        // POST /api/punch/request - Client requests NAT punch to a lobby
        app.MapPost("/api/punch/request", IResult (HttpContext context, PunchRequest request) =>
        {
            if (_punchServer == null || !_punchServer.IsRunning)
            {
                return Results.StatusCode(503); // Service Unavailable
            }
            
            if (request.LobbyId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "LobbyId is required" });
            }
            
            // Get client's external IP from request
            var externalIp = GetClientIpAddress(context);
            var clientExternalEndpoint = new IPEndPoint(IPAddress.Parse(externalIp), request.ClientPort);
            
            // Parse client's internal endpoint
            if (!IPEndPoint.TryParse(request.ClientInternalEndpoint, out var clientInternalEndpoint))
            {
                return Results.BadRequest(new { error = "Invalid client internal endpoint format" });
            }
            
            var lobbyIdStr = request.LobbyId.ToString();
            var clientToken = request.ClientToken ?? Guid.NewGuid().ToString("N");
            
            var result = _punchServer.RequestPunch(lobbyIdStr, clientInternalEndpoint, clientExternalEndpoint, clientToken);
            
            if (!result.Success)
            {
                Console.WriteLine($"[LobbyServer] Punch request failed for lobby {lobbyIdStr}: {result.Message}");
                return Results.NotFound(new { error = result.Message });
            }
            
            Console.WriteLine($"[LobbyServer] Punch initiated for lobby {lobbyIdStr}, token={result.PunchToken}");
            
            return Results.Ok(new PunchResponse(
                Success: true,
                PunchToken: result.PunchToken,
                Message: "Punch initiated - both sides should now send UDP packets"
            ));
        });
        
        // DELETE /api/punch/register/{lobbyId} - Unregister a host
        app.MapDelete("/api/punch/register/{lobbyId:guid}", (Guid lobbyId) =>
        {
            if (_punchServer == null)
            {
                return Results.Ok(new { unregistered = false });
            }
            
            _punchServer.UnregisterHost(lobbyId.ToString());
            return Results.Ok(new { unregistered = true });
        });
        
        // GET /api/punch/udp-status - Get UDP diagnostic information
        app.MapGet("/api/punch/udp-status", (HttpContext context) =>
        {
            var clientIp = GetClientIpAddress(context);
            var status = new
            {
                clientExternalIp = clientIp,
                punchServerRunning = _punchServer?.IsRunning ?? false,
                punchServerPort = _punchServer?.Port ?? 0,
                timestamp = DateTimeOffset.UtcNow
            };
            
            Console.WriteLine($"[LobbyServer] UDP status check from {clientIp}");
            
            return Results.Ok(status);
        });
        
        // POST /api/punch/send-test - Send a test UDP packet to specified endpoint (for debugging)
        app.MapPost("/api/punch/send-test", async (HttpContext context, UdpTestRequest request) =>
        {
            if (_punchServer == null || !_punchServer.IsRunning)
            {
                return Results.StatusCode(503);
            }
            
            var clientIp = GetClientIpAddress(context);
            Console.WriteLine($"[LobbyServer] UDP test requested from {clientIp}: target={request.TargetIp}:{request.TargetPort}");
            
            try
            {
                // Send a test UDP packet to the client's endpoint
                using var udpClient = new System.Net.Sockets.UdpClient();
                var message = System.Text.Encoding.UTF8.GetBytes($"YARG-UDP-TEST:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                var targetEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(request.TargetIp), request.TargetPort);
                
                int sentBytes = await udpClient.SendAsync(message, message.Length, targetEndpoint);
                
                Console.WriteLine($"[LobbyServer] Sent {sentBytes} bytes to {targetEndpoint}");
                
                return Results.Ok(new { sent = true, bytes = sentBytes, target = targetEndpoint.ToString() });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LobbyServer] UDP test failed: {ex.Message}");
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        
        // ========== RELAY ENDPOINTS ==========
        
        // GET /api/relay/info - Get relay server info
        app.MapGet("/api/relay/info", (HttpContext context) =>
        {
            if (_relayServer == null || !_relayServer.IsRunning)
            {
                return Results.Ok(new RelayInfoResponse(
                    Available: false,
                    Address: null,
                    Port: 0,
                    Message: "Relay server not available"
                ));
            }
            
            var serverHost = context.Request.Host.Host;
            if (serverHost == "localhost" || serverHost == "127.0.0.1")
            {
                serverHost = GetServerPublicAddress() ?? serverHost;
            }
            
            return Results.Ok(new RelayInfoResponse(
                Available: true,
                Address: serverHost,
                Port: _relayServer.Port,
                Message: "Relay server ready"
            ));
        });
        
        // POST /api/relay/allocate - Allocate a relay session for a lobby
        app.MapPost("/api/relay/allocate", (HttpContext context, RelayAllocateRequest request) =>
        {
            if (_relayServer == null || !_relayServer.IsRunning)
            {
                return Results.StatusCode(503);
            }
            
            if (request.LobbyId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "LobbyId is required" });
            }
            
            var clientIp = GetClientIpAddress(context);
            var result = _relayServer.AllocateSession(request.LobbyId, clientIp);
            
            if (!result.Success)
            {
                return Results.BadRequest(new { error = result.Message });
            }
            
            // Return the relay server's public address
            var serverHost = context.Request.Host.Host;
            if (serverHost == "localhost" || serverHost == "127.0.0.1")
            {
                serverHost = GetServerPublicAddress() ?? serverHost;
            }
            
            Console.WriteLine($"[LobbyServer] Relay session allocated: {result.SessionId} for lobby {request.LobbyId}");
            
            return Results.Ok(new RelayAllocateResponse(
                Success: true,
                SessionId: result.SessionId,
                RelayAddress: serverHost,
                RelayPort: result.RelayPort,
                Message: result.Message
            ));
        });
        
        // DELETE /api/relay/{sessionId} - Release a relay session
        app.MapDelete("/api/relay/{sessionId:guid}", (Guid sessionId) =>
        {
            if (_relayServer == null)
            {
                return Results.Ok(new { released = false });
            }
            
            _relayServer.ReleaseSession(sessionId);
            return Results.Ok(new { released = true });
        });
        
        // GET /api/relay/stats - Get relay server statistics
        app.MapGet("/api/relay/stats", () =>
        {
            if (_relayServer == null)
            {
                return Results.Ok(new { available = false });
            }
            
            var stats = _relayServer.GetStats();
            return Results.Ok(new
            {
                available = true,
                isRunning = stats.IsRunning,
                port = stats.Port,
                activeSessions = stats.ActiveSessions,
                totalSessionsCreated = stats.TotalSessionsCreated,
                packetsRelayed = stats.PacketsRelayed,
                bytesRelayed = stats.BytesRelayed
            });
        });

        Console.WriteLine("YARG LobbyServer Service starting...");
        Console.WriteLine("Endpoints:");
        Console.WriteLine("  GET    /api/lobbies             - List active lobbies");
        Console.WriteLine("  POST   /api/lobbies             - Register/heartbeat lobby");
        Console.WriteLine("  DELETE /api/lobbies/{id}        - Remove lobby");
        Console.WriteLine("  POST   /api/lobbies/code        - Generate code for lobby");
        Console.WriteLine("  GET    /api/lobbies/code/{code} - Look up lobby by code");
        Console.WriteLine("  DELETE /api/lobbies/code/{code} - Release code");
        Console.WriteLine("  GET    /api/punch/info          - Get NAT punch server info");
        Console.WriteLine("  POST   /api/punch/register      - Register host for punch");
        Console.WriteLine("  POST   /api/punch/request       - Request punch to lobby");
        Console.WriteLine("  GET    /api/relay/info          - Get relay server info");
        Console.WriteLine("  POST   /api/relay/allocate      - Allocate relay session");
        Console.WriteLine("  DELETE /api/relay/{sessionId}   - Release relay session");
        Console.WriteLine("  GET    /api/relay/stats         - Get relay statistics");
        Console.WriteLine();

        app.Run();
    }

    private static void PurgeStaleLobbies()
    {
        var cutoff = DateTimeOffset.UtcNow - LobbyTtl;
        var staleIds = Lobbies
            .Where(kvp => kvp.Value.LastSeen < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in staleIds)
        {
            if (Lobbies.TryRemove(id, out var removed))
            {
                // Clean up associated code
                CleanupLobbyCode(id);
                Console.WriteLine($"[LobbyServer] Lobby '{removed.LobbyName}' ({id}) expired (stale)");
            }
        }
    }

    private static void CleanupLobbyCode(Guid lobbyId)
    {
        if (LobbyIdToCodes.TryRemove(lobbyId, out var code))
        {
            LobbyCodes.TryRemove(code, out _);
            Console.WriteLine($"[LobbyServer] Code '{code}' cleaned up for lobby {lobbyId}");
        }
    }

    private static string GenerateUniqueCode()
    {
        const int maxAttempts = 100;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            var code = GenerateCode();
            
            // Try to reserve this code - if it already exists, try again
            if (LobbyCodes.TryAdd(code, Guid.Empty))
            {
                // We reserved it with a placeholder, it will be updated by the caller
                LobbyCodes.TryRemove(code, out _);
                return code;
            }
        }

        // Extremely unlikely but handle gracefully
        throw new InvalidOperationException("Unable to generate unique code after maximum attempts");
    }

    private static string GenerateCode()
    {
        // Generate 3 random bytes = 6 hex characters
        Span<byte> bytes = stackalloc byte[3];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers (behind proxy/load balancer)
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            // Take the first IP in the chain (original client)
            var firstIp = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(firstIp))
            {
                return firstIp;
            }
        }

        // Fall back to direct connection IP
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            // Handle IPv4-mapped IPv6 addresses
            if (remoteIp.IsIPv4MappedToIPv6)
            {
                return remoteIp.MapToIPv4().ToString();
            }
            return remoteIp.ToString();
        }

        return "0.0.0.0";
    }
    
    private static string? GetServerPublicAddress()
    {
        try
        {
            // Try to get the server's LAN IP for local testing
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    private sealed class LobbyRecord
    {
        public required Guid LobbyId { get; init; }
        public required string LobbyName { get; set; }
        public required string HostName { get; set; }
        public required string Address { get; set; }
        public required int Port { get; set; }
        public required int CurrentPlayers { get; set; }
        public required int MaxPlayers { get; set; }
        public required bool HasPassword { get; set; }
        public required string Version { get; set; }
        public required DateTimeOffset LastSeen { get; set; }
        public required DateTimeOffset CreatedAt { get; init; }

        public bool IsActive => DateTimeOffset.UtcNow - LastSeen < LobbyTtl;

        public LobbyDirectoryEntry ToDirectoryEntry()
        {
            return new LobbyDirectoryEntry(
                LobbyId: LobbyId,
                LobbyName: LobbyName,
                HostName: HostName,
                Address: Address,
                Port: Port,
                CurrentPlayers: CurrentPlayers,
                MaxPlayers: MaxPlayers,
                HasPassword: HasPassword,
                Version: Version,
                LastHeartbeatUtc: LastSeen);
        }
    }
}

// Request/Response types for lobby code endpoints
public record LobbyCodeRequest(Guid LobbyId);
public record LobbyCodeResponse(string Code, Guid LobbyId);

// Request/Response types for NAT punch endpoints
public record PunchInfoResponse(bool Available, string? Address, int Port, string Message);
public record PunchRegisterRequest(Guid LobbyId, string InternalEndpoint, int ExternalPort);
public record PunchRequest(Guid LobbyId, string ClientInternalEndpoint, int ClientPort, string? ClientToken = null);
public record PunchResponse(bool Success, string? PunchToken, string Message);

// UDP test endpoint request
public record UdpTestRequest(string TargetIp, int TargetPort);

// Request/Response types for relay endpoints
public record RelayInfoResponse(bool Available, string? Address, int Port, string Message);
public record RelayAllocateRequest(Guid LobbyId);
public record RelayAllocateResponse(bool Success, Guid SessionId, string RelayAddress, int RelayPort, string Message);
