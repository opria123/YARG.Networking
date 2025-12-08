using System;
using System.IO;
using System.Text;

namespace YARG.Net;

/// <summary>
/// Manages the local player's network identity, including persistence of the player ID.
/// </summary>
public static class LocalPlayerIdentity
{
    private static NetworkPlayerIdentity? _cachedIdentity;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the local player's identity, creating one if it doesn't exist.
    /// </summary>
    /// <param name="displayName">The display name to use.</param>
    /// <returns>The local player's network identity.</returns>
    public static NetworkPlayerIdentity GetOrCreate(string displayName)
    {
        lock (_lock)
        {
            if (_cachedIdentity is not null)
            {
                // Update display name if it changed
                if (_cachedIdentity.DisplayName != displayName)
                {
                    _cachedIdentity = _cachedIdentity.WithDisplayName(displayName);
                }
                return _cachedIdentity;
            }

            // Create a new identity with a new GUID
            _cachedIdentity = NetworkPlayerIdentity.CreateNew(displayName);
            return _cachedIdentity;
        }
    }

    /// <summary>
    /// Gets the local player's identity with a persisted player ID.
    /// </summary>
    /// <param name="displayName">The display name to use.</param>
    /// <param name="persistedPlayerId">The persisted player ID to use if available.</param>
    /// <returns>The local player's network identity.</returns>
    public static NetworkPlayerIdentity GetOrCreate(string displayName, Guid? persistedPlayerId)
    {
        lock (_lock)
        {
            if (_cachedIdentity is not null)
            {
                // Update display name if it changed
                if (_cachedIdentity.DisplayName != displayName)
                {
                    _cachedIdentity = _cachedIdentity.WithDisplayName(displayName);
                }
                return _cachedIdentity;
            }

            // Use persisted ID if available, otherwise create new
            if (persistedPlayerId.HasValue && persistedPlayerId.Value != Guid.Empty)
            {
                _cachedIdentity = NetworkPlayerIdentity.FromData(persistedPlayerId.Value, displayName);
            }
            else
            {
                _cachedIdentity = NetworkPlayerIdentity.CreateNew(displayName);
            }

            return _cachedIdentity;
        }
    }

    /// <summary>
    /// Gets the currently cached identity, if any.
    /// </summary>
    public static NetworkPlayerIdentity? Current
    {
        get
        {
            lock (_lock)
            {
                return _cachedIdentity;
            }
        }
    }

    /// <summary>
    /// Gets the player ID from the current identity, or null if not set.
    /// </summary>
    public static Guid? PlayerId
    {
        get
        {
            lock (_lock)
            {
                return _cachedIdentity?.PlayerId;
            }
        }
    }

    /// <summary>
    /// Gets the display name from the current identity, or null if not set.
    /// </summary>
    public static string? DisplayName
    {
        get
        {
            lock (_lock)
            {
                return _cachedIdentity?.DisplayName;
            }
        }
    }

    /// <summary>
    /// Updates the display name of the current identity.
    /// </summary>
    /// <param name="newDisplayName">The new display name.</param>
    /// <returns>True if the identity was updated, false if no identity exists.</returns>
    public static bool UpdateDisplayName(string newDisplayName)
    {
        lock (_lock)
        {
            if (_cachedIdentity is null)
            {
                return false;
            }

            _cachedIdentity = _cachedIdentity.WithDisplayName(newDisplayName);
            return true;
        }
    }

    /// <summary>
    /// Clears the cached identity. Used for testing or when logging out.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _cachedIdentity = null;
        }
    }

    /// <summary>
    /// Generates a random player name for anonymous/guest users.
    /// </summary>
    /// <returns>A random player name like "Player_1234".</returns>
    public static string GenerateRandomDisplayName()
    {
        var random = new Random();
        return $"Player_{random.Next(1000, 9999)}";
    }
}
