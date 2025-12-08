using System;
using System.Collections.Generic;
using YARG.Net.Transport;

namespace YARG.Net.Runtime;

/// <summary>
/// Tracks connected clients and manages the authentication/handshake lifecycle.
/// Shared between hosted game servers and dedicated servers.
/// </summary>
public interface IServerConnectionManager
{
    /// <summary>
    /// Raised when a client completes authentication and is ready for gameplay.
    /// </summary>
    event EventHandler<ServerClientAuthenticatedEventArgs>? ClientAuthenticated;

    /// <summary>
    /// Raised when a client disconnects (either gracefully or due to timeout).
    /// </summary>
    event EventHandler<ServerClientDisconnectedEventArgs>? ClientDisconnected;

    /// <summary>
    /// Raised when a client fails authentication.
    /// </summary>
    event EventHandler<ServerClientAuthFailedEventArgs>? ClientAuthenticationFailed;

    /// <summary>
    /// Gets all authenticated clients.
    /// </summary>
    IReadOnlyDictionary<Guid, ServerClientInfo> AuthenticatedClients { get; }

    /// <summary>
    /// Gets all pending (unauthenticated) connections.
    /// </summary>
    IReadOnlyDictionary<Guid, INetConnection> PendingConnections { get; }

    /// <summary>
    /// Gets the count of authenticated clients.
    /// </summary>
    int AuthenticatedClientCount { get; }

    /// <summary>
    /// Gets the total connection count (pending + authenticated).
    /// </summary>
    int TotalConnectionCount { get; }

    /// <summary>
    /// Called when a new peer connects at the transport level.
    /// </summary>
    void OnPeerConnected(INetConnection connection);

    /// <summary>
    /// Called when a peer disconnects at the transport level.
    /// </summary>
    void OnPeerDisconnected(INetConnection connection);

    /// <summary>
    /// Marks a pending connection as authenticated with the given player identity.
    /// </summary>
    /// <param name="connectionId">The connection ID to authenticate.</param>
    /// <param name="identity">The player's network identity.</param>
    /// <returns>True if the connection was found and authenticated, false otherwise.</returns>
    bool AuthenticateClient(Guid connectionId, NetworkPlayerIdentity identity);

    /// <summary>
    /// Rejects a pending connection (e.g., failed password check).
    /// </summary>
    /// <param name="connectionId">The connection ID to reject.</param>
    /// <param name="reason">The reason for rejection.</param>
    void RejectClient(Guid connectionId, string reason);

    /// <summary>
    /// Disconnects an authenticated client.
    /// </summary>
    /// <param name="connectionId">The connection ID to disconnect.</param>
    /// <param name="reason">Optional reason for disconnection.</param>
    void DisconnectClient(Guid connectionId, string? reason = null);

    /// <summary>
    /// Disconnects all clients (used during server shutdown).
    /// </summary>
    /// <param name="reason">Optional reason for disconnection.</param>
    void DisconnectAll(string? reason = null);

    /// <summary>
    /// Gets a client by connection ID.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>The client info, or null if not found.</returns>
    ServerClientInfo? GetClient(Guid connectionId);

    /// <summary>
    /// Gets a client by player ID.
    /// </summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <returns>The client info, or null if not found.</returns>
    ServerClientInfo? GetClientByPlayerId(Guid playerId);

    /// <summary>
    /// Gets a connection (either pending or authenticated) by ID.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>The connection, or null if not found.</returns>
    INetConnection? GetConnection(Guid connectionId);

    /// <summary>
    /// Clears all connections (used during server reset).
    /// </summary>
    void Clear();
}

/// <summary>
/// Information about an authenticated client.
/// </summary>
public sealed class ServerClientInfo
{
    public ServerClientInfo(INetConnection connection, NetworkPlayerIdentity identity)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ConnectionId = connection.Id;
        ConnectedAt = DateTime.UtcNow;
        AuthenticatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The underlying network connection.
    /// </summary>
    public INetConnection Connection { get; }

    /// <summary>
    /// The connection's unique ID (transport-level, changes per connection).
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// The player's network identity (persistent ID + display name).
    /// </summary>
    public NetworkPlayerIdentity Identity { get; }

    /// <summary>
    /// The player's unique ID (persistent across sessions).
    /// </summary>
    public Guid PlayerId => Identity.PlayerId;

    /// <summary>
    /// The player's display name.
    /// </summary>
    public string DisplayName => Identity.DisplayName;

    /// <summary>
    /// When the client first connected.
    /// </summary>
    public DateTime ConnectedAt { get; }

    /// <summary>
    /// When the client completed authentication.
    /// </summary>
    public DateTime AuthenticatedAt { get; }

    /// <summary>
    /// The client's current latency in milliseconds.
    /// </summary>
    public int LatencyMs { get; set; }
}

/// <summary>
/// Event args for when a client completes authentication.
/// </summary>
public sealed class ServerClientAuthenticatedEventArgs : EventArgs
{
    public ServerClientAuthenticatedEventArgs(ServerClientInfo client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ServerClientInfo Client { get; }
}

/// <summary>
/// Event args for when a client disconnects.
/// </summary>
public sealed class ServerClientDisconnectedEventArgs : EventArgs
{
    public ServerClientDisconnectedEventArgs(Guid connectionId, NetworkPlayerIdentity? identity, bool wasAuthenticated)
    {
        ConnectionId = connectionId;
        Identity = identity;
        WasAuthenticated = wasAuthenticated;
    }

    public Guid ConnectionId { get; }
    
    /// <summary>
    /// The player's identity, or null if they disconnected before authentication.
    /// </summary>
    public NetworkPlayerIdentity? Identity { get; }
    
    /// <summary>
    /// The player's display name, or null if they disconnected before authentication.
    /// </summary>
    public string? DisplayName => Identity?.DisplayName;
    
    /// <summary>
    /// The player's unique ID, or null if they disconnected before authentication.
    /// </summary>
    public Guid? PlayerId => Identity?.PlayerId;
    
    public bool WasAuthenticated { get; }
}

/// <summary>
/// Event args for when a client fails authentication.
/// </summary>
public sealed class ServerClientAuthFailedEventArgs : EventArgs
{
    public ServerClientAuthFailedEventArgs(Guid connectionId, string reason)
    {
        ConnectionId = connectionId;
        Reason = reason ?? string.Empty;
    }

    public Guid ConnectionId { get; }
    public string Reason { get; }
}
