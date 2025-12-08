using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Net.Transport;

namespace YARG.Net.Runtime;

/// <summary>
/// Default implementation of <see cref="IServerConnectionManager"/> that tracks
/// connected clients through the authentication lifecycle.
/// </summary>
public sealed class ServerConnectionManager : IServerConnectionManager
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, INetConnection> _pendingConnections = new();
    private readonly Dictionary<Guid, ServerClientInfo> _authenticatedClients = new();
    private readonly Dictionary<Guid, INetConnection> _allConnections = new();
    private readonly Dictionary<Guid, Guid> _playerIdToConnectionId = new(); // PlayerId -> ConnectionId mapping

    public event EventHandler<ServerClientAuthenticatedEventArgs>? ClientAuthenticated;
    public event EventHandler<ServerClientDisconnectedEventArgs>? ClientDisconnected;
    public event EventHandler<ServerClientAuthFailedEventArgs>? ClientAuthenticationFailed;

    public IReadOnlyDictionary<Guid, ServerClientInfo> AuthenticatedClients
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<Guid, ServerClientInfo>(_authenticatedClients);
            }
        }
    }

    public IReadOnlyDictionary<Guid, INetConnection> PendingConnections
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<Guid, INetConnection>(_pendingConnections);
            }
        }
    }

    public int AuthenticatedClientCount
    {
        get
        {
            lock (_lock)
            {
                return _authenticatedClients.Count;
            }
        }
    }

    public int TotalConnectionCount
    {
        get
        {
            lock (_lock)
            {
                return _allConnections.Count;
            }
        }
    }

    public void OnPeerConnected(INetConnection connection)
    {
        if (connection is null)
        {
            return;
        }

        lock (_lock)
        {
            _allConnections[connection.Id] = connection;
            _pendingConnections[connection.Id] = connection;
        }
    }

    public void OnPeerDisconnected(INetConnection connection)
    {
        if (connection is null)
        {
            return;
        }

        ServerClientInfo? clientInfo = null;
        bool wasAuthenticated = false;

        lock (_lock)
        {
            _allConnections.Remove(connection.Id);
            _pendingConnections.Remove(connection.Id);

            if (_authenticatedClients.TryGetValue(connection.Id, out clientInfo))
            {
                _authenticatedClients.Remove(connection.Id);
                _playerIdToConnectionId.Remove(clientInfo.PlayerId);
                wasAuthenticated = true;
            }
        }

        ClientDisconnected?.Invoke(this, new ServerClientDisconnectedEventArgs(
            connection.Id,
            clientInfo?.Identity,
            wasAuthenticated));
    }

    public bool AuthenticateClient(Guid connectionId, NetworkPlayerIdentity identity)
    {
        if (identity is null)
        {
            return false;
        }

        INetConnection? connection;
        ServerClientInfo clientInfo;

        lock (_lock)
        {
            if (!_pendingConnections.TryGetValue(connectionId, out connection))
            {
                return false;
            }

            _pendingConnections.Remove(connectionId);
            clientInfo = new ServerClientInfo(connection, identity);
            _authenticatedClients[connectionId] = clientInfo;
            _playerIdToConnectionId[identity.PlayerId] = connectionId;
        }

        ClientAuthenticated?.Invoke(this, new ServerClientAuthenticatedEventArgs(clientInfo));
        return true;
    }

    public void RejectClient(Guid connectionId, string reason)
    {
        INetConnection? connection;
        ServerClientInfo? clientInfo = null;

        lock (_lock)
        {
            if (!_pendingConnections.TryGetValue(connectionId, out connection))
            {
                // Try authenticated clients as well
                if (_authenticatedClients.TryGetValue(connectionId, out clientInfo))
                {
                    connection = clientInfo.Connection;
                    _playerIdToConnectionId.Remove(clientInfo.PlayerId);
                }
            }

            _pendingConnections.Remove(connectionId);
            _authenticatedClients.Remove(connectionId);
            _allConnections.Remove(connectionId);
        }

        ClientAuthenticationFailed?.Invoke(this, new ServerClientAuthFailedEventArgs(connectionId, reason));

        // Disconnect the underlying connection
        connection?.Disconnect(reason);
    }

    public void DisconnectClient(Guid connectionId, string? reason = null)
    {
        INetConnection? connection = null;
        ServerClientInfo? clientInfo = null;
        bool wasAuthenticated = false;

        lock (_lock)
        {
            if (_pendingConnections.TryGetValue(connectionId, out connection))
            {
                _pendingConnections.Remove(connectionId);
            }
            else if (_authenticatedClients.TryGetValue(connectionId, out clientInfo))
            {
                connection = clientInfo.Connection;
                _authenticatedClients.Remove(connectionId);
                _playerIdToConnectionId.Remove(clientInfo.PlayerId);
                wasAuthenticated = true;
            }

            _allConnections.Remove(connectionId);
        }

        if (connection is not null)
        {
            connection.Disconnect(reason);
            ClientDisconnected?.Invoke(this, new ServerClientDisconnectedEventArgs(
                connectionId,
                clientInfo?.Identity,
                wasAuthenticated));
        }
    }

    public void DisconnectAll(string? reason = null)
    {
        List<(Guid Id, INetConnection Connection, ServerClientInfo? Info, bool WasAuthenticated)> toDisconnect;

        lock (_lock)
        {
            toDisconnect = new List<(Guid, INetConnection, ServerClientInfo?, bool)>(_allConnections.Count);

            foreach (var kvp in _pendingConnections)
            {
                toDisconnect.Add((kvp.Key, kvp.Value, null, false));
            }

            foreach (var kvp in _authenticatedClients)
            {
                toDisconnect.Add((kvp.Key, kvp.Value.Connection, kvp.Value, true));
            }

            _pendingConnections.Clear();
            _authenticatedClients.Clear();
            _allConnections.Clear();
            _playerIdToConnectionId.Clear();
        }

        foreach (var item in toDisconnect)
        {
            try
            {
                item.Connection.Disconnect(reason);
            }
            catch
            {
                // Ignore disconnect errors during shutdown
            }

            ClientDisconnected?.Invoke(this, new ServerClientDisconnectedEventArgs(
                item.Id,
                item.Info?.Identity,
                item.WasAuthenticated));
        }
    }

    public ServerClientInfo? GetClient(Guid connectionId)
    {
        lock (_lock)
        {
            _authenticatedClients.TryGetValue(connectionId, out var clientInfo);
            return clientInfo;
        }
    }

    public ServerClientInfo? GetClientByPlayerId(Guid playerId)
    {
        lock (_lock)
        {
            if (_playerIdToConnectionId.TryGetValue(playerId, out var connectionId))
            {
                _authenticatedClients.TryGetValue(connectionId, out var clientInfo);
                return clientInfo;
            }
            return null;
        }
    }

    public INetConnection? GetConnection(Guid connectionId)
    {
        lock (_lock)
        {
            _allConnections.TryGetValue(connectionId, out var connection);
            return connection;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pendingConnections.Clear();
            _authenticatedClients.Clear();
            _allConnections.Clear();
            _playerIdToConnectionId.Clear();
        }
    }

    /// <summary>
    /// Updates the latency for a client connection.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="latencyMs">The latency in milliseconds.</param>
    public void UpdateLatency(Guid connectionId, int latencyMs)
    {
        lock (_lock)
        {
            if (_authenticatedClients.TryGetValue(connectionId, out var clientInfo))
            {
                clientInfo.LatencyMs = latencyMs;
            }
        }
    }

    /// <summary>
    /// Broadcasts a message to all authenticated clients.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="channel">The channel type.</param>
    public void BroadcastToAuthenticated(ReadOnlySpan<byte> data, ChannelType channel = ChannelType.ReliableOrdered)
    {
        List<INetConnection> connections;

        lock (_lock)
        {
            connections = new List<INetConnection>(_authenticatedClients.Count);
            foreach (var kvp in _authenticatedClients)
            {
                connections.Add(kvp.Value.Connection);
            }
        }

        var dataArray = data.ToArray();
        foreach (var connection in connections)
        {
            try
            {
                connection.Send(dataArray, channel);
            }
            catch
            {
                // Ignore send errors
            }
        }
    }

    /// <summary>
    /// Broadcasts a message to all authenticated clients except one.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="excludeConnectionId">The connection ID to exclude.</param>
    /// <param name="channel">The channel type.</param>
    public void BroadcastToAuthenticatedExcept(ReadOnlySpan<byte> data, Guid excludeConnectionId, ChannelType channel = ChannelType.ReliableOrdered)
    {
        List<INetConnection> connections;

        lock (_lock)
        {
            connections = new List<INetConnection>(_authenticatedClients.Count);
            foreach (var kvp in _authenticatedClients)
            {
                if (kvp.Key != excludeConnectionId)
                {
                    connections.Add(kvp.Value.Connection);
                }
            }
        }

        var dataArray = data.ToArray();
        foreach (var connection in connections)
        {
            try
            {
                connection.Send(dataArray, channel);
            }
            catch
            {
                // Ignore send errors
            }
        }
    }

    /// <summary>
    /// Broadcasts a message to all authenticated clients except one (by player ID).
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="excludePlayerId">The player ID to exclude.</param>
    /// <param name="channel">The channel type.</param>
    public void BroadcastToAuthenticatedExceptPlayer(ReadOnlySpan<byte> data, Guid excludePlayerId, ChannelType channel = ChannelType.ReliableOrdered)
    {
        List<INetConnection> connections;

        lock (_lock)
        {
            connections = new List<INetConnection>(_authenticatedClients.Count);
            foreach (var kvp in _authenticatedClients)
            {
                if (kvp.Value.PlayerId != excludePlayerId)
                {
                    connections.Add(kvp.Value.Connection);
                }
            }
        }

        var dataArray = data.ToArray();
        foreach (var connection in connections)
        {
            try
            {
                connection.Send(dataArray, channel);
            }
            catch
            {
                // Ignore send errors
            }
        }
    }
}
