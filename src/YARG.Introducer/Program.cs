using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YARG.Net.Directory;

namespace YARG.Introducer;

internal static class Program
{
    private static readonly ConcurrentDictionary<Guid, LobbyRecord> Lobbies = new();

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

        // Health check
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

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

            Console.WriteLine($"[Introducer] Lobby '{request.LobbyName}' ({request.LobbyId}) registered/updated from {address}:{request.Port}");
            
            return Results.Ok(record.ToDirectoryEntry());
        });

        // DELETE /api/lobbies/{id} - Remove a lobby
        app.MapDelete("/api/lobbies/{id:guid}", (Guid id) =>
        {
            if (Lobbies.TryRemove(id, out var removed))
            {
                Console.WriteLine($"[Introducer] Lobby '{removed.LobbyName}' ({id}) removed");
                return Results.Ok(new { removed = true });
            }
            
            return Results.NotFound(new { error = "Lobby not found" });
        });

        Console.WriteLine("YARG Introducer Service starting...");
        Console.WriteLine("Endpoints:");
        Console.WriteLine("  GET  /api/lobbies        - List active lobbies");
        Console.WriteLine("  POST /api/lobbies        - Register/heartbeat lobby");
        Console.WriteLine("  DELETE /api/lobbies/{id} - Remove lobby");
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
                Console.WriteLine($"[Introducer] Lobby '{removed.LobbyName}' ({id}) expired (stale)");
            }
        }
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
