using System;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Transport;

namespace YARG.Net.Runtime;

/// <summary>
/// Server runtime that manages the transport lifecycle and connection tracking.
/// Supports both hosted game servers (where host is also a player) and dedicated servers.
/// </summary>
public interface IServerRuntime
{
    /// <summary>
    /// Raised when a new peer connects at the transport level.
    /// </summary>
    event EventHandler<ServerPeerConnectedEventArgs>? PeerConnected;

    /// <summary>
    /// Raised when a peer disconnects at the transport level.
    /// </summary>
    event EventHandler<ServerPeerDisconnectedEventArgs>? PeerDisconnected;

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the connection manager for tracking authenticated clients.
    /// </summary>
    IServerConnectionManager? ConnectionManager { get; }

    /// <summary>
    /// Configures the server runtime with the specified options.
    /// Must be called before <see cref="StartAsync"/>.
    /// </summary>
    void Configure(ServerRuntimeOptions options);

    /// <summary>
    /// Starts the server and begins accepting connections.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully stops the server, disconnecting all clients.
    /// </summary>
    /// <param name="reason">Optional reason for shutdown to send to clients.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a message to all authenticated clients.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="channel">The channel type.</param>
    void Broadcast(ReadOnlySpan<byte> data, ChannelType channel = ChannelType.ReliableOrdered);

    /// <summary>
    /// Broadcasts a message to all authenticated clients except one.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="excludeConnectionId">The connection ID to exclude.</param>
    /// <param name="channel">The channel type.</param>
    void BroadcastExcept(ReadOnlySpan<byte> data, Guid excludeConnectionId, ChannelType channel = ChannelType.ReliableOrdered);
}

/// <summary>
/// Options for configuring a server runtime.
/// </summary>
public sealed record ServerRuntimeOptions
{
    public ServerRuntimeOptions(INetTransport transport)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// The transport layer to use.
    /// </summary>
    public INetTransport Transport { get; init; }

    /// <summary>
    /// The port to listen on.
    /// </summary>
    public int Port { get; init; } = 7777;

    /// <summary>
    /// The address to bind to.
    /// </summary>
    public string Address { get; init; } = "0.0.0.0";

    /// <summary>
    /// Whether to enable NAT punch-through.
    /// </summary>
    public bool EnableNatPunchThrough { get; init; }

    /// <summary>
    /// Optional packet dispatcher for handling incoming packets.
    /// </summary>
    public IPacketDispatcher? PacketDispatcher { get; init; }

    /// <summary>
    /// Optional connection manager. If not provided, a default one will be created.
    /// </summary>
    public IServerConnectionManager? ConnectionManager { get; init; }

    /// <summary>
    /// Whether this is a dedicated server (no local host player).
    /// </summary>
    public bool IsDedicatedServer { get; init; }

    /// <summary>
    /// Maximum number of connected players allowed.
    /// </summary>
    public int MaxPlayers { get; init; } = 32;
}

/// <summary>
/// Event args for when a peer connects to the server.
/// </summary>
public sealed class ServerPeerConnectedEventArgs : EventArgs
{
    public ServerPeerConnectedEventArgs(INetConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public INetConnection Connection { get; }
}

/// <summary>
/// Event args for when a peer disconnects from the server.
/// </summary>
public sealed class ServerPeerDisconnectedEventArgs : EventArgs
{
    public ServerPeerDisconnectedEventArgs(INetConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public INetConnection Connection { get; }
}
