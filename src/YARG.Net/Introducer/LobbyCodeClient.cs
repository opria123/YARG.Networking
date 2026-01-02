using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Directory;

namespace YARG.Net.Introducer
{
    /// <summary>
    /// Client for interacting with introducer lobby code endpoints.
    /// Lobby codes are 6-character hex strings that allow players to easily join lobbies.
    /// </summary>
    public sealed class LobbyCodeClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        /// <summary>
        /// Creates a new LobbyCodeClient with the default HttpClient.
        /// </summary>
        public LobbyCodeClient()
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Creates a new LobbyCodeClient with a shared HttpClient.
        /// </summary>
        /// <param name="httpClient">The HttpClient to use. Will not be disposed by this client.</param>
        public LobbyCodeClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = false;
        }

        /// <summary>
        /// Registers a lobby with the introducer service.
        /// This must be called before generating a code for the lobby.
        /// </summary>
        /// <param name="introducerUrl">The base URL of the introducer service.</param>
        /// <param name="lobbyId">The lobby ID.</param>
        /// <param name="lobbyName">The display name of the lobby.</param>
        /// <param name="hostName">The host's display name.</param>
        /// <param name="hostAddress">The public IP address of the host.</param>
        /// <param name="hostPort">The port the host is listening on.</param>
        /// <param name="maxPlayers">Maximum number of players allowed.</param>
        /// <param name="hasPassword">Whether the lobby requires a password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if registration succeeded.</returns>
        public async Task<bool> RegisterLobbyAsync(
            string introducerUrl,
            Guid lobbyId,
            string lobbyName,
            string hostName,
            string hostAddress,
            int hostPort,
            int maxPlayers,
            bool hasPassword,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(introducerUrl))
                return false;

            if (lobbyId == Guid.Empty)
                return false;

            try
            {
                var url = introducerUrl.TrimEnd('/') + "/api/lobbies";
                var request = new
                {
                    LobbyId = lobbyId,
                    LobbyName = lobbyName ?? "Unnamed Lobby",
                    HostName = hostName ?? "Unknown Host",
                    Address = hostAddress ?? "0.0.0.0",
                    Port = hostPort,
                    CurrentPlayers = 1,
                    MaxPlayers = maxPlayers,
                    HasPassword = hasPassword,
                    Version = "1.0"
                };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a lobby code for the specified lobby.
        /// If the lobby already has a code, the existing code is returned.
        /// Note: The lobby must be registered first via RegisterLobbyAsync.
        /// </summary>
        /// <param name="introducerUrl">The base URL of the introducer service.</param>
        /// <param name="lobbyId">The lobby ID to generate a code for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The generated (or existing) lobby code result.</returns>
        public async Task<LobbyCodeResult> GenerateCodeAsync(
            string introducerUrl,
            Guid lobbyId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(introducerUrl))
                throw new ArgumentException("Introducer URL is required", nameof(introducerUrl));

            if (lobbyId == Guid.Empty)
                throw new ArgumentException("Lobby ID is required", nameof(lobbyId));

            try
            {
                var url = BuildCodeUrl(introducerUrl);
                var request = new { LobbyId = lobbyId };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var codeResponse = JsonSerializer.Deserialize<LobbyCodeResponse>(responseJson, JsonOptions);

                    if (codeResponse != null && !string.IsNullOrEmpty(codeResponse.Code))
                    {
                        return LobbyCodeResult.Success(codeResponse.Code, codeResponse.LobbyId);
                    }
                }

                var errorJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var error = TryParseError(errorJson);
                return LobbyCodeResult.Failure(error ?? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            catch (TaskCanceledException)
            {
                return LobbyCodeResult.Failure("Request timed out");
            }
            catch (HttpRequestException ex)
            {
                return LobbyCodeResult.Failure($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return LobbyCodeResult.Failure($"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers an existing lobby code with an introducer.
        /// This is used to disseminate a code generated by another introducer
        /// so players using different introducers can still find each other.
        /// </summary>
        /// <param name="introducerUrl">The base URL of the introducer service.</param>
        /// <param name="code">The 6-character lobby code to register.</param>
        /// <param name="lobbyId">The lobby ID this code maps to.</param>
        /// <param name="hostAddress">The public IP address of the host.</param>
        /// <param name="hostPort">The port the host is listening on.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the code was registered successfully.</returns>
        public async Task<bool> RegisterCodeAsync(
            string introducerUrl,
            string code,
            Guid lobbyId,
            string hostAddress,
            int hostPort,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(introducerUrl))
                return false;

            if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
                return false;

            if (lobbyId == Guid.Empty)
                return false;

            try
            {
                var url = BuildCodeUrl(introducerUrl) + "/register";
                var request = new 
                { 
                    Code = code.ToUpperInvariant(),
                    LobbyId = lobbyId,
                    HostAddress = hostAddress,
                    HostPort = hostPort
                };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Looks up a lobby by its code.
        /// </summary>
        /// <param name="introducerUrl">The base URL of the introducer service.</param>
        /// <param name="code">The 6-character lobby code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The lobby lookup result.</returns>
        public async Task<LobbyLookupResult> LookupCodeAsync(
            string introducerUrl,
            string code,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(introducerUrl))
                throw new ArgumentException("Introducer URL is required", nameof(introducerUrl));

            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code is required", nameof(code));

            code = code.Trim().ToUpperInvariant();
            if (code.Length != 6)
                return LobbyLookupResult.Failure("Code must be 6 characters");

            try
            {
                var url = BuildCodeUrl(introducerUrl, code);
                var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var entry = JsonSerializer.Deserialize<LobbyDirectoryEntry>(responseJson, JsonOptions);

                    if (entry != null)
                    {
                        // Pass the introducer URL for NAT punch coordination
                        return LobbyLookupResult.Success(entry, introducerUrl);
                    }
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return LobbyLookupResult.Failure("Invalid or expired code");
                }

                var errorJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var error = TryParseError(errorJson);
                return LobbyLookupResult.Failure(error ?? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            catch (TaskCanceledException)
            {
                return LobbyLookupResult.Failure("Request timed out");
            }
            catch (HttpRequestException ex)
            {
                return LobbyLookupResult.Failure($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return LobbyLookupResult.Failure($"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases a lobby code.
        /// </summary>
        /// <param name="introducerUrl">The base URL of the introducer service.</param>
        /// <param name="code">The 6-character lobby code to release.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the code was released, false otherwise.</returns>
        public async Task<bool> ReleaseCodeAsync(
            string introducerUrl,
            string code,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(introducerUrl))
                throw new ArgumentException("Introducer URL is required", nameof(introducerUrl));

            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Trim().ToUpperInvariant();
            if (code.Length != 6)
                return false;

            try
            {
                var url = BuildCodeUrl(introducerUrl, code);
                var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildCodeUrl(string baseUrl, string? code = null)
        {
            var url = baseUrl.TrimEnd('/') + "/api/lobbies/code";
            if (!string.IsNullOrEmpty(code))
            {
                url += "/" + code;
            }
            return url;
        }

        private static string? TryParseError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    return errorProp.GetString();
                }
            }
            catch
            {
                // Ignore parse errors
            }
            return null;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        private sealed class LobbyCodeResponse
        {
            public string Code { get; set; } = string.Empty;
            public Guid LobbyId { get; set; }
        }
    }

    /// <summary>
    /// Result of a lobby code generation request.
    /// </summary>
    public sealed class LobbyCodeResult
    {
        public bool IsSuccess { get; }
        public string? Code { get; }
        public Guid LobbyId { get; }
        public string? Error { get; }

        private LobbyCodeResult(bool isSuccess, string? code, Guid lobbyId, string? error)
        {
            IsSuccess = isSuccess;
            Code = code;
            LobbyId = lobbyId;
            Error = error;
        }

        public static LobbyCodeResult Success(string code, Guid lobbyId) =>
            new(true, code, lobbyId, null);

        public static LobbyCodeResult Failure(string error) =>
            new(false, null, Guid.Empty, error);
    }

    /// <summary>
    /// Result of a lobby code lookup request.
    /// </summary>
    public sealed class LobbyLookupResult
    {
        public bool IsSuccess { get; }
        public LobbyDirectoryEntry? Lobby { get; }
        public string? Error { get; }
        
        /// <summary>
        /// The introducer URL that was used for the successful lookup.
        /// Used for NAT punch coordination.
        /// </summary>
        public string? IntroducerUrl { get; }

        private LobbyLookupResult(bool isSuccess, LobbyDirectoryEntry? lobby, string? error, string? introducerUrl = null)
        {
            IsSuccess = isSuccess;
            Lobby = lobby;
            Error = error;
            IntroducerUrl = introducerUrl;
        }

        public static LobbyLookupResult Success(LobbyDirectoryEntry lobby, string? introducerUrl = null) =>
            new(true, lobby, null, introducerUrl);

        public static LobbyLookupResult Failure(string error) =>
            new(false, null, error);
    }
}
