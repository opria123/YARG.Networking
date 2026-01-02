using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;

namespace YARG.Net.Directory;

/// <summary>
/// Client for NAT punch-through coordination with the lobby server service.
/// Uses HTTP for signaling and LiteNetLib's NatPunchModule for the actual UDP hole punching.
/// </summary>
public sealed class NatPunchClient : IDisposable, INatPunchListener
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    private readonly HttpClient _httpClient;
    private readonly Uri _introducerBaseUri;
    private NetManager? _punchManager;
    private readonly EventBasedNetListener _listener;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    
    /// <summary>
    /// Fired when NAT punch succeeds and a connection endpoint is available.
    /// </summary>
    public event Action<IPEndPoint, NatAddressType, string>? OnPunchSuccess;
    
    /// <summary>
    /// Fired when a NAT punch attempt fails or times out.
    /// </summary>
#pragma warning disable CS0067 // Event is never used - will be used for timeout handling
    public event Action<string, string>? OnPunchFailed; // token, reason
#pragma warning restore CS0067
    
    /// <summary>
    /// Gets the local port being used for punch-through.
    /// </summary>
    public int LocalPort => _punchManager?.LocalPort ?? 0;
    
    /// <summary>
    /// Gets whether the punch client is active.
    /// </summary>
    public bool IsActive => _punchManager?.IsRunning == true;
    
    /// <summary>
    /// Creates a new NAT punch client.
    /// </summary>
    /// <param name="introducerBaseUri">Base URI of the lobby server service (e.g., https://lobby.yarg.in)</param>
    /// <param name="httpClient">Optional HttpClient for HTTP requests</param>
    public NatPunchClient(Uri introducerBaseUri, HttpClient? httpClient = null)
    {
        _introducerBaseUri = introducerBaseUri;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _listener = new EventBasedNetListener();
    }
    
    /// <summary>
    /// Gets information about the lobby server's NAT punch server.
    /// </summary>
    public async Task<PunchServerInfo> GetPunchServerInfoAsync(CancellationToken ct = default)
    {
        var uri = new Uri(_introducerBaseUri, "/api/punch/info");
        var response = await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<PunchServerInfo>(json, JsonOptions) 
            ?? throw new InvalidOperationException("Invalid punch server info response");
    }
    
    /// <summary>
    /// Starts the UDP socket for NAT punch communication.
    /// </summary>
    /// <param name="localPort">Local port to bind to (0 = auto)</param>
    public void Start(int localPort = 0)
    {
        if (_punchManager != null && _punchManager.IsRunning)
        {
            return;
        }
        
        _punchManager = new NetManager(_listener)
        {
            NatPunchEnabled = true,
            UnconnectedMessagesEnabled = true,
            IPv6Enabled = false,
        };
        
        _punchManager.NatPunchModule.Init(this);
        
        if (!_punchManager.Start(localPort))
        {
            throw new InvalidOperationException($"Failed to start NAT punch client on port {localPort}");
        }
        
        // Start poll loop
        _pollCts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_pollCts.Token);
        
        Console.WriteLine($"[NatPunchClient] Started on port {_punchManager.LocalPort}");
    }
    
    /// <summary>
    /// Registers this client as a host for NAT punch coordination.
    /// Call this after creating a lobby to allow clients to punch through.
    /// </summary>
    /// <param name="lobbyId">The lobby ID</param>
    /// <param name="gamePort">The game server port (for informational purposes - the punch uses the client's UDP port)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<bool> RegisterAsHostAsync(Guid lobbyId, int gamePort, CancellationToken ct = default)
    {
        if (_punchManager == null || !_punchManager.IsRunning)
        {
            Console.WriteLine("[NatPunchClient] Cannot register as host: not started");
            return false;
        }
        
        try
        {
            // Get local endpoint info - use the punch client's port, not the game port
            // NAT punch happens on the punch client's UDP socket, not the game server's socket
            var localIp = GetLocalIp();
            var localEndpoint = $"{localIp}:{_punchManager.LocalPort}";
            
            // Use the punch client's port for external registration - this is the port NAT will see
            var request = new PunchRegisterRequest(lobbyId, localEndpoint, _punchManager.LocalPort);
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var uri = new Uri(_introducerBaseUri, "/api/punch/register");
            var response = await _httpClient.PostAsync(uri, content, ct).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[NatPunchClient] Failed to register as host: {response.StatusCode} - {error}");
                return false;
            }
            
            Console.WriteLine($"[NatPunchClient] Registered as host for lobby {lobbyId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NatPunchClient] Error registering as host: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Unregisters this host from NAT punch coordination.
    /// Call this when closing a lobby.
    /// </summary>
    public async Task UnregisterAsHostAsync(Guid lobbyId, CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri(_introducerBaseUri, $"/api/punch/register/{lobbyId}");
            await _httpClient.DeleteAsync(uri, ct).ConfigureAwait(false);
            Console.WriteLine($"[NatPunchClient] Unregistered as host for lobby {lobbyId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NatPunchClient] Error unregistering as host: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Initiates a NAT punch to connect to a lobby.
    /// The punch server will coordinate with the host and both sides will attempt to punch through.
    /// </summary>
    /// <param name="lobbyId">The lobby ID to connect to</param>
    /// <param name="clientToken">Optional client identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The punch token if successful, null if failed</returns>
    public async Task<PunchInitiationResult> InitiatePunchAsync(Guid lobbyId, string? clientToken = null, CancellationToken ct = default)
    {
        if (_punchManager == null || !_punchManager.IsRunning)
        {
            return new PunchInitiationResult(false, null, "NAT punch client not started");
        }
        
        try
        {
            // Get local endpoint info
            var localIp = GetLocalIp();
            var localEndpoint = $"{localIp}:{_punchManager.LocalPort}";
            
            var request = new PunchRequest(lobbyId, localEndpoint, _punchManager.LocalPort, clientToken);
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var uri = new Uri(_introducerBaseUri, "/api/punch/request");
            var response = await _httpClient.PostAsync(uri, content, ct).ConfigureAwait(false);
            
            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[NatPunchClient] Punch request failed: {response.StatusCode} - {responseJson}");
                return new PunchInitiationResult(false, null, $"Server error: {response.StatusCode}");
            }
            
            var result = JsonSerializer.Deserialize<PunchResponseDto>(responseJson, JsonOptions);
            if (result == null || !result.Success)
            {
                return new PunchInitiationResult(false, null, result?.Message ?? "Unknown error");
            }
            
            Console.WriteLine($"[NatPunchClient] Punch initiated, token={result.PunchToken}");
            return new PunchInitiationResult(true, result.PunchToken, "Punch initiated");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NatPunchClient] Error initiating punch: {ex.Message}");
            return new PunchInitiationResult(false, null, ex.Message);
        }
    }
    
    /// <summary>
    /// Sends a NAT introduction request directly to the punch server.
    /// This is an alternative to HTTP-based initiation for lower latency.
    /// </summary>
    public void SendNatIntroduceRequest(string punchServerHost, int punchServerPort, string token)
    {
        if (_punchManager == null || !_punchManager.IsRunning)
        {
            Console.WriteLine("[NatPunchClient] Cannot send NAT introduce request: not started");
            return;
        }
        
        Console.WriteLine($"[NatPunchClient] Sending NAT introduce request to {punchServerHost}:{punchServerPort}");
        _punchManager.NatPunchModule.SendNatIntroduceRequest(punchServerHost, punchServerPort, token);
    }
    
    /// <summary>
    /// Stops the NAT punch client.
    /// </summary>
    public void Stop()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        
        _punchManager?.Stop();
        _punchManager = null;
        
        Console.WriteLine("[NatPunchClient] Stopped");
    }
    
    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
    
    // INatPunchListener implementation
    
    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        // This is only called on the punch server, not on clients
        Console.WriteLine($"[NatPunchClient] NAT introduction request (unexpected): local={localEndPoint}, remote={remoteEndPoint}");
    }
    
    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        Console.WriteLine($"[NatPunchClient] NAT punch SUCCESS: target={targetEndPoint}, type={type}, token={token}");
        OnPunchSuccess?.Invoke(targetEndPoint, type, token);
    }
    
    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _punchManager != null)
        {
            try
            {
                _punchManager.PollEvents();
                _punchManager.NatPunchModule.PollEvents();
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NatPunchClient] Poll error: {ex.Message}");
            }
        }
    }
    
    private static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            if (socket.LocalEndPoint is IPEndPoint endpoint)
            {
                return endpoint.Address.ToString();
            }
        }
        catch
        {
            // Fallback
        }
        
        return "127.0.0.1";
    }
}

// DTOs for HTTP communication

/// <summary>
/// Information about the lobby server's NAT punch server.
/// </summary>
public record PunchServerInfo(bool Available, string? Address, int Port, string Message);

/// <summary>
/// Request to register a host for NAT punch coordination.
/// </summary>
public record PunchRegisterRequest(Guid LobbyId, string InternalEndpoint, int ExternalPort);

/// <summary>
/// Request to initiate a NAT punch to a lobby.
/// </summary>
public record PunchRequest(Guid LobbyId, string ClientInternalEndpoint, int ClientPort, string? ClientToken = null);

/// <summary>
/// Response from punch initiation.
/// </summary>
internal record PunchResponseDto(bool Success, string? PunchToken, string Message);

/// <summary>
/// Result of a punch initiation attempt.
/// </summary>
public record PunchInitiationResult(bool Success, string? PunchToken, string Message);
