using System;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using YARG.Net.Transport;

namespace YARG.Net.Discovery;

/// <summary>
/// Handles discovery requests on the server side.
/// Responds to discovery pings from clients so they can find the server.
/// </summary>
public sealed class ServerDiscoveryHandler
{
    private readonly LiteNetLibTransport _transport;
    private DiscoveryLobbyInfo? _advertisedLobby;
    private bool _isAdvertising;
    
    /// <summary>
    /// Creates a new server discovery handler.
    /// </summary>
    /// <param name="transport">The transport to use for sending responses.</param>
    public ServerDiscoveryHandler(LiteNetLibTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }
    
    /// <summary>
    /// Starts listening for discovery requests.
    /// </summary>
    public void StartListening()
    {
        _transport.OnUnconnectedMessage += HandleUnconnectedMessage;
        Console.WriteLine("[ServerDiscovery] Started listening for discovery requests");
    }
    
    /// <summary>
    /// Stops listening for discovery requests.
    /// </summary>
    public void StopListening()
    {
        _transport.OnUnconnectedMessage -= HandleUnconnectedMessage;
        _isAdvertising = false;
        Console.WriteLine("[ServerDiscovery] Stopped listening for discovery requests");
    }
    
    /// <summary>
    /// Starts advertising the lobby for discovery.
    /// </summary>
    /// <param name="lobby">The lobby information to advertise.</param>
    public void StartAdvertising(DiscoveryLobbyInfo lobby)
    {
        _advertisedLobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
        _isAdvertising = true;
        Console.WriteLine($"[ServerDiscovery] Started advertising lobby: {lobby.LobbyName} on port {lobby.Port}");
    }
    
    /// <summary>
    /// Stops advertising the lobby.
    /// </summary>
    public void StopAdvertising()
    {
        _advertisedLobby = null;
        _isAdvertising = false;
        Console.WriteLine("[ServerDiscovery] Stopped advertising");
    }
    
    /// <summary>
    /// Updates the player count in the advertised lobby info.
    /// </summary>
    public void UpdatePlayerCount(int currentPlayers)
    {
        if (_advertisedLobby != null)
        {
            _advertisedLobby.CurrentPlayers = currentPlayers;
        }
    }
    
    private void HandleUnconnectedMessage(IPEndPoint remoteEndPoint, byte[] data)
    {
        Console.WriteLine($"[ServerDiscovery] Received unconnected message from {remoteEndPoint}, {data.Length} bytes, isAdvertising={_isAdvertising}, hasLobby={_advertisedLobby != null}");
        
        if (!_isAdvertising || _advertisedLobby == null)
        {
            Console.WriteLine("[ServerDiscovery] Not advertising, ignoring message");
            return;
        }
        
        // Check if this is a discovery request
        if (!Directory.DiscoveryProtocol.IsRequest(data))
        {
            Console.WriteLine($"[ServerDiscovery] Not a discovery request (first bytes: {(data.Length > 0 ? data[0].ToString("X2") : "empty")})");
            return;
        }
        
        Console.WriteLine($"[ServerDiscovery] Valid discovery request from {remoteEndPoint}");
        SendDiscoveryResponse(remoteEndPoint);
    }
    
    private void SendDiscoveryResponse(IPEndPoint remoteEndPoint)
    {
        if (_advertisedLobby == null || _transport.NetManager == null)
        {
            return;
        }
        
        try
        {
            // Convert to DiscoveredLobbyInfo for the builder
            var lobbyInfo = new Directory.DiscoveredLobbyInfo
            {
                LobbyId = _advertisedLobby.LobbyId,
                LobbyName = _advertisedLobby.LobbyName,
                HostName = _advertisedLobby.HostName,
                CurrentPlayers = _advertisedLobby.CurrentPlayers,
                MaxPlayers = _advertisedLobby.MaxPlayers,
                HasPassword = _advertisedLobby.HasPassword,
                PrivacyMode = (Directory.LobbyPrivacy)_advertisedLobby.PrivacyMode,
                Port = _advertisedLobby.Port,
                PublicPort = _advertisedLobby.PublicPort,
                PublicAddress = _advertisedLobby.PublicAddress ?? string.Empty,
                TransportId = "LiteNetLib",
                PlayerNames = _advertisedLobby.PlayerNames ?? Array.Empty<string>(),
                PlayerInstruments = _advertisedLobby.PlayerInstruments ?? Array.Empty<int>(),
                NoFailMode = _advertisedLobby.NoFailMode,
                SharedSongsOnly = _advertisedLobby.SharedSongsOnly,
                BandSize = _advertisedLobby.BandSize,
                AllowedGameModes = _advertisedLobby.AllowedGameModes ?? Array.Empty<int>(),
                SessionType = _advertisedLobby.SessionType,
                IsDedicatedServer = _advertisedLobby.IsDedicatedServer
            };
            
            // Build response packet
            var responseBytes = new Directory.DiscoveryResponseBuilder()
                .WithLobbyInfo(lobbyInfo)
                .Build();
            
            // Send via LiteNetLib
            var writer = new NetDataWriter();
            writer.Put(responseBytes);
            _transport.NetManager.SendUnconnectedMessage(writer, remoteEndPoint);
            
            Console.WriteLine($"[ServerDiscovery] Sent discovery response to {remoteEndPoint}: {_advertisedLobby.LobbyName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerDiscovery] Failed to send discovery response: {ex.Message}");
        }
    }
}

/// <summary>
/// Lobby information for server-side discovery advertising.
/// </summary>
public sealed class DiscoveryLobbyInfo
{
    public string LobbyId { get; set; } = Guid.NewGuid().ToString();
    public string LobbyName { get; set; } = "YARG Server";
    public string HostName { get; set; } = "Dedicated Server";
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; } = 8;
    public bool HasPassword { get; set; }
    public int PrivacyMode { get; set; }
    public int Port { get; set; } = 7777;
    public int PublicPort { get; set; }
    public string? PublicAddress { get; set; }
    public string[]? PlayerNames { get; set; }
    public int[]? PlayerInstruments { get; set; }
    public bool NoFailMode { get; set; } = true;
    public bool SharedSongsOnly { get; set; } = true;
    public int BandSize { get; set; }
    public int[]? AllowedGameModes { get; set; }
    public int SessionType { get; set; }
    public bool IsDedicatedServer { get; set; } = true;
}
