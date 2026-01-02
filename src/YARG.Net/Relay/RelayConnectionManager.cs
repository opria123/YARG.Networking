using System;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Relay;

/// <summary>
/// Manages relay connections for when direct P2P is not possible.
/// This is the main entry point for the relay system.
/// </summary>
public sealed class RelayConnectionManager : IDisposable
{
    private readonly string _introducerUrl;
    private RelayHttpClient? _httpClient;
    private RelayClient? _relayClient;
    private Guid _currentSessionId;
    private bool _isHost;
    
    /// <summary>Event fired when data is received from the peer.</summary>
    public event Action<byte[]>? OnDataReceived;
    
    /// <summary>Event fired when connected via relay.</summary>
    public event Action? OnConnected;
    
    /// <summary>Event fired when disconnected.</summary>
    public event Action? OnDisconnected;
    
    /// <summary>Event fired on errors.</summary>
    public event Action<string>? OnError;
    
    public bool IsConnected => _relayClient?.IsPeerConnected ?? false;
    public bool IsRelayActive => _relayClient != null && _relayClient.IsRegistered;
    public Guid SessionId => _currentSessionId;
    
    public RelayConnectionManager(string introducerUrl)
    {
        _introducerUrl = introducerUrl;
    }
    
    /// <summary>
    /// Checks if the relay server is available.
    /// </summary>
    public async Task<bool> IsRelayAvailableAsync(CancellationToken ct = default)
    {
        _httpClient ??= new RelayHttpClient(_introducerUrl);
        
        var info = await _httpClient.GetRelayInfoAsync(ct);
        return info?.Available ?? false;
    }
    
    /// <summary>
    /// Allocates a relay session and starts the host side of the connection.
    /// Call this when hosting a lobby that needs relay.
    /// </summary>
    /// <param name="lobbyId">The lobby ID to create a relay for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The relay session info, or null if allocation failed.</returns>
    public async Task<RelayAllocation?> StartHostRelayAsync(Guid lobbyId, CancellationToken ct = default)
    {
        _httpClient ??= new RelayHttpClient(_introducerUrl);
        
        var allocation = await _httpClient.AllocateSessionAsync(lobbyId, ct);
        if (allocation == null || !allocation.Success)
        {
            OnError?.Invoke("Failed to allocate relay session");
            return null;
        }
        
        _currentSessionId = allocation.SessionId;
        _isHost = true;
        
        // Create and start relay client as host
        _relayClient = new RelayClient(
            allocation.RelayAddress!,
            allocation.RelayPort,
            allocation.SessionId,
            isHost: true);
        
        _relayClient.OnDataReceived += data => OnDataReceived?.Invoke(data);
        _relayClient.OnPeerConnected += () => OnConnected?.Invoke();
        _relayClient.OnPeerDisconnected += () => OnDisconnected?.Invoke();
        _relayClient.OnError += msg => OnError?.Invoke(msg);
        
        _relayClient.Start();
        
        Console.WriteLine($"[RelayManager] Host relay started: session={allocation.SessionId}, relay={allocation.RelayAddress}:{allocation.RelayPort}");
        
        return allocation;
    }
    
    /// <summary>
    /// Connects to a lobby via relay as a client.
    /// </summary>
    /// <param name="lobbyId">The lobby ID to connect to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if connection initiated successfully.</returns>
    public async Task<bool> ConnectViaRelayAsync(Guid lobbyId, CancellationToken ct = default)
    {
        _httpClient ??= new RelayHttpClient(_introducerUrl);
        
        // Client also allocates (which returns the existing session for the lobby)
        var allocation = await _httpClient.AllocateSessionAsync(lobbyId, ct);
        if (allocation == null || !allocation.Success)
        {
            OnError?.Invoke("Failed to get relay session info");
            return false;
        }
        
        _currentSessionId = allocation.SessionId;
        _isHost = false;
        
        // Create and start relay client as client
        _relayClient = new RelayClient(
            allocation.RelayAddress!,
            allocation.RelayPort,
            allocation.SessionId,
            isHost: false);
        
        _relayClient.OnDataReceived += data => OnDataReceived?.Invoke(data);
        _relayClient.OnPeerConnected += () => OnConnected?.Invoke();
        _relayClient.OnPeerDisconnected += () => OnDisconnected?.Invoke();
        _relayClient.OnError += msg => OnError?.Invoke(msg);
        
        _relayClient.Start();
        
        Console.WriteLine($"[RelayManager] Client relay started: session={allocation.SessionId}, relay={allocation.RelayAddress}:{allocation.RelayPort}");
        
        return true;
    }
    
    /// <summary>
    /// Sends data through the relay to the peer.
    /// </summary>
    public void Send(byte[] data)
    {
        _relayClient?.Send(data);
    }
    
    /// <summary>
    /// Disconnects from the relay.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _relayClient?.Disconnect();
        _relayClient = null;
        
        if (_currentSessionId != Guid.Empty && _isHost)
        {
            // Host releases the session
            await (_httpClient?.ReleaseSessionAsync(_currentSessionId) ?? Task.CompletedTask);
        }
        
        _currentSessionId = Guid.Empty;
    }
    
    public void Dispose()
    {
        _relayClient?.Dispose();
        _httpClient?.Dispose();
    }
}
