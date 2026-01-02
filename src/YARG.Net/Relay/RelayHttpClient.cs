using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Relay;

/// <summary>
/// HTTP client for interacting with the relay server's REST API.
/// Handles session allocation and management.
/// </summary>
public sealed class RelayHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    
    public RelayHttpClient(string introducerUrl)
    {
        _baseUrl = introducerUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }
    
    /// <summary>
    /// Gets information about the relay server.
    /// </summary>
    public async Task<RelayInfo?> GetRelayInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/relay/info", ct);
            if (!response.IsSuccessStatusCode)
                return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<RelayInfo>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RelayHttpClient] GetRelayInfo failed: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Allocates a relay session for a lobby.
    /// </summary>
    public async Task<RelayAllocation?> AllocateSessionAsync(Guid lobbyId, CancellationToken ct = default)
    {
        try
        {
            var request = new { lobbyId };
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/relay/allocate", content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[RelayHttpClient] Allocate failed: {error}");
                return null;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<RelayAllocation>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RelayHttpClient] AllocateSession failed: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Releases a relay session.
    /// </summary>
    public async Task<bool> ReleaseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/relay/{sessionId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RelayHttpClient] ReleaseSession failed: {ex.Message}");
            return false;
        }
    }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Relay server info returned by /api/relay/info
/// </summary>
public class RelayInfo
{
    public bool Available { get; set; }
    public string? Address { get; set; }
    public int Port { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Relay session allocation result from /api/relay/allocate
/// </summary>
public class RelayAllocation
{
    public bool Success { get; set; }
    public Guid SessionId { get; set; }
    public string? RelayAddress { get; set; }
    public int RelayPort { get; set; }
    public string? Message { get; set; }
}
