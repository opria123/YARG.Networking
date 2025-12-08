using System;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Serialization;
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
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer InitializeHostedServer(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        TimeSpan? pollInterval = null)
    {
        return Initialize(transport, serializer, port, maxPlayers, isDedicatedServer: false, pollInterval);
    }

    /// <summary>
    /// Initializes a server networking stack for a dedicated server (no local host player).
    /// </summary>
    /// <param name="transport">The transport layer to use.</param>
    /// <param name="serializer">The packet serializer.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="maxPlayers">Maximum number of players allowed.</param>
    /// <param name="pollInterval">Optional polling interval.</param>
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer InitializeDedicatedServer(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        TimeSpan? pollInterval = null)
    {
        return Initialize(transport, serializer, port, maxPlayers, isDedicatedServer: true, pollInterval);
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
    /// <returns>A configured server networking instance.</returns>
    public static ServerNetworkingServer Initialize(
        INetTransport transport,
        INetSerializer serializer,
        int port = 7777,
        int maxPlayers = 32,
        bool isDedicatedServer = false,
        TimeSpan? pollInterval = null)
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

        var options = new ServerRuntimeOptions(transport)
        {
            Port = port,
            Address = "0.0.0.0",
            EnableNatPunchThrough = true,
            PacketDispatcher = dispatcher,
            ConnectionManager = connectionManager,
            IsDedicatedServer = isDedicatedServer,
            MaxPlayers = maxPlayers,
        };

        runtime.Configure(options);

        return new ServerNetworkingServer(
            runtime,
            dispatcher,
            connectionManager,
            isDedicatedServer,
            maxPlayers);
    }
}

/// <summary>
/// Encapsulates all server networking components.
/// </summary>
public sealed class ServerNetworkingServer
{
    public ServerNetworkingServer(
        IServerRuntime runtime,
        IPacketDispatcher packetDispatcher,
        IServerConnectionManager connectionManager,
        bool isDedicatedServer,
        int maxPlayers)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        PacketDispatcher = packetDispatcher ?? throw new ArgumentNullException(nameof(packetDispatcher));
        ConnectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        IsDedicatedServer = isDedicatedServer;
        MaxPlayers = maxPlayers;
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
    /// Gets the current number of authenticated clients.
    /// </summary>
    public int CurrentPlayerCount => ConnectionManager.AuthenticatedClientCount + (IsDedicatedServer ? 0 : 1);

    /// <summary>
    /// Gets whether the server has capacity for more players.
    /// </summary>
    public bool HasCapacity => CurrentPlayerCount < MaxPlayers;
}
