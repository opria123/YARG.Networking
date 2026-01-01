using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using YARG.Net.Packets;
using YARG.Net.Transport;

namespace YARG.Net.Sessions;

/// <summary>
/// Manages password authentication for password-protected lobbies.
/// Handles authentication requests and tracks authenticated connections.
/// </summary>
public sealed class LobbyAuthenticator
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _authenticatedConnections = new();
    private readonly Dictionary<Guid, int> _failedAttempts = new();
    
    private string? _passwordHash;
    private bool _requiresPassword;
    private int _maxPlayers;
    private int _currentPlayerCount;
    private readonly int _maxFailedAttempts;

    /// <summary>
    /// Creates a new LobbyAuthenticator.
    /// </summary>
    /// <param name="maxFailedAttempts">Maximum failed attempts before lockout. Default is 5.</param>
    public LobbyAuthenticator(int maxFailedAttempts = 5)
    {
        _maxFailedAttempts = maxFailedAttempts;
    }

    /// <summary>
    /// Gets whether the lobby requires password authentication.
    /// </summary>
    public bool RequiresPassword
    {
        get
        {
            lock (_gate)
            {
                return _requiresPassword;
            }
        }
    }

    /// <summary>
    /// Gets the number of authenticated connections.
    /// </summary>
    public int AuthenticatedCount
    {
        get
        {
            lock (_gate)
            {
                return _authenticatedConnections.Count;
            }
        }
    }

    /// <summary>
    /// Sets the lobby password. Pass null or empty to disable password protection.
    /// </summary>
    public void SetPassword(string? password)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(password))
            {
                _passwordHash = null;
                _requiresPassword = false;
            }
            else
            {
                _passwordHash = HashPassword(password);
                _requiresPassword = true;
            }
        }
    }

    /// <summary>
    /// Sets the player capacity for the lobby.
    /// </summary>
    public void SetCapacity(int maxPlayers, int currentPlayers)
    {
        lock (_gate)
        {
            _maxPlayers = maxPlayers;
            _currentPlayerCount = currentPlayers;
        }
    }

    /// <summary>
    /// Updates the current player count.
    /// </summary>
    public void UpdatePlayerCount(int currentPlayers)
    {
        lock (_gate)
        {
            _currentPlayerCount = currentPlayers;
        }
    }

    /// <summary>
    /// Checks if a connection is already authenticated.
    /// </summary>
    public bool IsAuthenticated(Guid connectionId)
    {
        lock (_gate)
        {
            // If no password required, everyone is "authenticated"
            if (!_requiresPassword)
            {
                return true;
            }
            return _authenticatedConnections.Contains(connectionId);
        }
    }

    /// <summary>
    /// Validates a password without tracking connection state.
    /// Useful when connection GUID is not available.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <param name="resultMessage">Output message describing the result.</param>
    /// <returns>True if the password is valid (or no password is required).</returns>
    public bool Validate(string? password, out string resultMessage)
    {
        lock (_gate)
        {
            // If no password required, always valid
            if (!_requiresPassword)
            {
                resultMessage = "No password required";
                return true;
            }

            // Check lobby capacity
            if (_currentPlayerCount >= _maxPlayers)
            {
                resultMessage = "Lobby is full";
                return false;
            }

            // Verify password
            var providedHash = HashPassword(password ?? string.Empty);
            if (!string.Equals(_passwordHash, providedHash, StringComparison.Ordinal))
            {
                resultMessage = "Incorrect password";
                return false;
            }

            resultMessage = "Success";
            return true;
        }
    }

    /// <summary>
    /// Processes an authentication request.
    /// </summary>
    /// <returns>The authentication result.</returns>
    public AuthResult ProcessAuthRequest(Guid connectionId, string? password)
    {
        lock (_gate)
        {
            // Check if already authenticated
            if (_authenticatedConnections.Contains(connectionId))
            {
                return AuthResult.Success;
            }

            // Check for too many failed attempts
            if (_failedAttempts.TryGetValue(connectionId, out var attempts) && attempts >= _maxFailedAttempts)
            {
                return AuthResult.Banned;
            }

            // Check lobby capacity
            if (_currentPlayerCount >= _maxPlayers)
            {
                return AuthResult.LobbyFull;
            }

            // If no password required, auto-authenticate
            if (!_requiresPassword)
            {
                _authenticatedConnections.Add(connectionId);
                AuthenticationSucceeded?.Invoke(this, new AuthenticationEventArgs(connectionId));
                return AuthResult.Success;
            }

            // Verify password
            var providedHash = HashPassword(password ?? string.Empty);
            if (!string.Equals(_passwordHash, providedHash, StringComparison.Ordinal))
            {
                // Track failed attempt
                _failedAttempts[connectionId] = attempts + 1;
                AuthenticationFailed?.Invoke(this, new AuthenticationEventArgs(connectionId, AuthResult.WrongPassword));
                return AuthResult.WrongPassword;
            }

            // Success!
            _authenticatedConnections.Add(connectionId);
            _failedAttempts.Remove(connectionId);
            AuthenticationSucceeded?.Invoke(this, new AuthenticationEventArgs(connectionId));
            return AuthResult.Success;
        }
    }

    /// <summary>
    /// Removes authentication for a connection (e.g., when they disconnect).
    /// </summary>
    public void RemoveConnection(Guid connectionId)
    {
        lock (_gate)
        {
            _authenticatedConnections.Remove(connectionId);
            _failedAttempts.Remove(connectionId);
        }
    }

    /// <summary>
    /// Clears all authentication state.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _authenticatedConnections.Clear();
            _failedAttempts.Clear();
        }
    }

    /// <summary>
    /// Builds an AuthResponsePacket for the given result.
    /// </summary>
    public AuthResponsePacket BuildResponse(Guid sessionId, AuthResult result)
    {
        string? errorMessage = result switch
        {
            AuthResult.WrongPassword => "Incorrect password",
            AuthResult.LobbyFull => "Lobby is full",
            AuthResult.LobbyNotFound => "Lobby no longer exists",
            AuthResult.Banned => "Too many failed attempts",
            _ => null
        };

        return new AuthResponsePacket(sessionId, result, errorMessage);
    }

    private static string HashPassword(string password)
    {
        // Use SHA256 for password hashing (simple, good enough for lobby passwords)
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Raised when authentication succeeds.
    /// </summary>
    public event EventHandler<AuthenticationEventArgs>? AuthenticationSucceeded;

    /// <summary>
    /// Raised when authentication fails.
    /// </summary>
    public event EventHandler<AuthenticationEventArgs>? AuthenticationFailed;
}

/// <summary>
/// Event args for authentication events.
/// </summary>
public sealed class AuthenticationEventArgs : EventArgs
{
    public AuthenticationEventArgs(Guid connectionId, AuthResult result = AuthResult.Success)
    {
        ConnectionId = connectionId;
        Result = result;
    }

    public Guid ConnectionId { get; }
    public AuthResult Result { get; }
}
