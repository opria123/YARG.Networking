using System;
using YARG.Net.Discovery;
using YARG.Net.Handlers;
using YARG.Net.Handlers.Server;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Serialization;
using YARG.Net.Sessions;
using YARG.Net.Transport;

namespace YARG.Net.Runtime;

/// <summary>
/// Helper for wiring up the default server runtime, packet dispatcher, and connection management.
/// Supports both hosted game servers and dedicated servers.
/// </summary>
public static class ServerNetworkingBootstrapper
{
    /// <summary>
    /// Initializes a server networking stack for a hosted game server (host is also a player).
    /// </summary>
    /// <param name="transport">The transport layer to use.</param>
    /// <param name="serializer">The packet serializer.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="maxPlayers">Maximum number of players allowed.</param>
    /// <param name="pollInterval">Optional polling interval.</param>
    /// <param name="password">Optional lobby password.</param>
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer InitializeHostedServer(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        TimeSpan? pollInterval = null,
        string? password = null)
    {
        return Initialize(transport, serializer, port, maxPlayers, isDedicatedServer: false, pollInterval, password);
    }

    /// <summary>
    /// Initializes a server networking stack for a dedicated server (no local host player).
    /// </summary>
    /// <param name="transport">The transport layer to use.</param>
    /// <param name="serializer">The packet serializer.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="maxPlayers">Maximum number of players allowed.</param>
    /// <param name="pollInterval">Optional polling interval.</param>
    /// <param name="password">Optional lobby password.</param>
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer InitializeDedicatedServer(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        TimeSpan? pollInterval = null,
        string? password = null)
    {
        return Initialize(transport, serializer, port, maxPlayers, isDedicatedServer: true, pollInterval, password);
    }

    /// <summary>
    /// Initializes a server networking stack with full configuration options.
    /// </summary>
    /// <param name="transport">The transport layer to use.</param>
    /// <param name="serializer">The packet serializer.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="maxPlayers">Maximum number of players allowed.</param>
    /// <param name="isDedicatedServer">Whether this is a dedicated server.</param>
    /// <param name="pollInterval">Optional polling interval.</param>
    /// <param name="password">Optional lobby password.</param>
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer Initialize(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        bool isDedicatedServer = false,
        TimeSpan? pollInterval = null,
        string? password = null)
    {
        if (transport is null)
        {
            throw new ArgumentNullException(nameof(transport));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        var runtime = new DefaultServerRuntime(pollInterval);
        var dispatcher = new PacketDispatcher(serializer);
        var connectionManager = new ServerConnectionManager();
        var sessionManager = new SessionManager(maxPlayers);

        // Create and register handshake handler
        var handshakeHandler = new ServerHandshakeHandler(
            sessionManager,
            serializer,
            new HandshakeServerOptions
            {
                Password = password,
            });
        handshakeHandler.Register(dispatcher);

        // Wire up handshake events to connection manager
        handshakeHandler.HandshakeAccepted += (_, session) =>
        {
            // Convert SessionRecord to NetworkPlayerIdentity
            var identity = NetworkPlayerIdentity.FromData(session.SessionId, session.PlayerName);
            connectionManager.AuthenticateClient(session.Connection.Id, identity);
        };

        // Create binary packet relay for handling gameplay snapshots and other binary packets.
        // ONLY for dedicated servers - in hosted mode, LiteNetNetworkingAdapter handles packet relay directly.
        ServerBinaryPacketRelay? binaryPacketRelay = null;
        ServerDiscoveryHandler? discoveryHandler = null;
        if (isDedicatedServer)
        {
            binaryPacketRelay = new ServerBinaryPacketRelay(sessionManager);
            
            // Create discovery handler for dedicated servers so clients can find this server
            if (transport is LiteNetLibTransport liteNetTransport)
            {
                discoveryHandler = new ServerDiscoveryHandler(liteNetTransport);
            }
        }

        var options = new ServerRuntimeOptions(transport)
        {
            Port = port,
            Address = "0.0.0.0",
            EnableNatPunchThrough = true,
            PacketDispatcher = dispatcher,
            ConnectionManager = connectionManager,
            BinaryPacketRelay = binaryPacketRelay,
            IsDedicatedServer = isDedicatedServer,
            MaxPlayers = maxPlayers,
        };

        runtime.Configure(options);

        return new ServerNetworkingServer(
            runtime,
            dispatcher,
            connectionManager,
            isDedicatedServer,
            maxPlayers,
            port,
            password,
            sessionManager,
            handshakeHandler,
            discoveryHandler);
    }
}

/// <summary>
/// Encapsulates all server networking components.
/// </summary>
public sealed class ServerNetworkingServer
{
    private readonly int _port;
    private readonly string? _password;
    private readonly ServerDiscoveryHandler? _discoveryHandler;
    private string _lobbyName = "YARG Server";
    
    /// <summary>
    /// Creates a server networking instance with full configuration.
    /// </summary>
    public ServerNetworkingServer(
        IServerRuntime runtime,
        IPacketDispatcher packetDispatcher,
        IServerConnectionManager connectionManager,
        bool isDedicatedServer,
        int maxPlayers,
        int port = 7777,
        string? password = null,
        SessionManager? sessionManager = null,
        ServerHandshakeHandler? handshakeHandler = null,
        ServerDiscoveryHandler? discoveryHandler = null)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        PacketDispatcher = packetDispatcher ?? throw new ArgumentNullException(nameof(packetDispatcher));
        ConnectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        IsDedicatedServer = isDedicatedServer;
        MaxPlayers = maxPlayers;
        _port = port;
        _password = password;
        SessionManager = sessionManager;
        HandshakeHandler = handshakeHandler;
        _discoveryHandler = discoveryHandler;
    }

    /// <summary>
    /// The server runtime that manages transport and polling.
    /// </summary>
    public IServerRuntime Runtime { get; }

    /// <summary>
    /// The packet dispatcher for handling incoming packets.
    /// </summary>
    public IPacketDispatcher PacketDispatcher { get; }

    /// <summary>
    /// The connection manager for tracking clients.
    /// </summary>
    public IServerConnectionManager ConnectionManager { get; }

    /// <summary>
    /// Whether this is a dedicated server (no local host player).
    /// </summary>
    public bool IsDedicatedServer { get; }

    /// <summary>
    /// Maximum number of players allowed.
    /// </summary>
    public int MaxPlayers { get; }

    /// <summary>
    /// The session manager for player sessions.
    /// </summary>
    public SessionManager? SessionManager { get; }

    /// <summary>
    /// The handshake handler for authentication.
    /// </summary>
    public ServerHandshakeHandler? HandshakeHandler { get; }

    /// <summary>
    /// Gets the current number of authenticated clients.
    /// </summary>
    public int CurrentPlayerCount => ConnectionManager.AuthenticatedClientCount + (IsDedicatedServer ? 0 : 1);

    /// <summary>
    /// Gets whether the server has capacity for more players.
    /// </summary>
    public bool HasCapacity => CurrentPlayerCount < MaxPlayers;
    
    /// <summary>
    /// Gets or sets the lobby name for discovery.
    /// </summary>
    public string LobbyName
    {
        get => _lobbyName;
        set
        {
            _lobbyName = value;
            // Re-advertise with new name if discovery is active
            if (_discoveryHandler != null && IsDedicatedServer)
            {
                StartDiscovery();
            }
        }
    }
    
    /// <summary>
    /// Starts discovery advertising so clients can find this server.
    /// Call this after the server runtime has started.
    /// </summary>
    public void StartDiscovery()
    {
        if (_discoveryHandler == null)
        {
            return;
        }
        
        _discoveryHandler.StartListening();
        
        var lobbyInfo = new DiscoveryLobbyInfo
        {
            LobbyId = Guid.NewGuid().ToString(),
            LobbyName = _lobbyName,
            HostName = "Dedicated Server",
            CurrentPlayers = CurrentPlayerCount,
            MaxPlayers = MaxPlayers,
            HasPassword = !string.IsNullOrEmpty(_password),
            PrivacyMode = 0, // Public
            Port = _port,
            IsDedicatedServer = true,
            NoFailMode = true,
            SharedSongsOnly = true,
        };
        
        _discoveryHandler.StartAdvertising(lobbyInfo);
    }
    
    /// <summary>
    /// Stops discovery advertising.
    /// </summary>
    public void StopDiscovery()
    {
        _discoveryHandler?.StopAdvertising();
        _discoveryHandler?.StopListening();
    }
    
    /// <summary>
    /// Updates the player count in discovery advertisements.
    /// </summary>
    public void UpdateDiscoveryPlayerCount()
    {
        _discoveryHandler?.UpdatePlayerCount(CurrentPlayerCount);
    }
}
