using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YARG.Net;

/// <summary>
/// Manages the local player's network identity, including persistence of the player ID.
/// Supports multiple local profiles, each with their own unique network identity.
/// </summary>
public static class LocalPlayerIdentity
{
    /// <summary>
    /// Cache of identities by profile ID. Each profile gets its own unique network identity.
    /// </summary>
    private static readonly Dictionary<Guid, NetworkPlayerIdentity> _identityCache = new();
    
    /// <summary>
    /// Legacy single-identity cache for backwards compatibility when no profile ID is provided.
    /// </summary>
    private static NetworkPlayerIdentity? _defaultIdentity;
    
    private static readonly object _lock = new();
    
    /// <summary>
    /// Unique session ID generated once per process. This ensures each ParrelSync instance
    /// or separate game instance gets a unique network identity even if sharing profile folders.
    /// Used only for the default identity when no profile ID is provided.
    /// </summary>
    private static readonly Guid _sessionId = Guid.NewGuid();

    /// <summary>
    /// Gets the local player's identity for a specific profile, creating one if it doesn't exist.
    /// Each profile gets a unique NetworkPlayerId that combines the session ID and profile ID,
    /// ensuring uniqueness across different game instances even with the same profiles.
    /// </summary>
    /// <param name="displayName">The display name to use.</param>
    /// <param name="profileId">The profile's unique ID.</param>
    /// <returns>The network identity for this profile.</returns>
    public static NetworkPlayerIdentity GetOrCreate(string displayName, Guid profileId)
    {
        lock (_lock)
        {
            // Check if we already have an identity for this profile
            if (_identityCache.TryGetValue(profileId, out var existingIdentity))
            {
                // Update display name if it changed
                if (existingIdentity.DisplayName != displayName)
                {
                    existingIdentity = existingIdentity.WithDisplayName(displayName);
                    _identityCache[profileId] = existingIdentity;
                }
                return existingIdentity;
            }

            // Create a unique NetworkPlayerId by combining session ID and profile ID
            // This ensures different game instances get unique IDs even with identical profiles
            // (e.g., host and client with synced profiles, or ParrelSync clones)
            var networkPlayerId = CombineGuids(_sessionId, profileId);
            var newIdentity = NetworkPlayerIdentity.FromData(networkPlayerId, displayName);
            _identityCache[profileId] = newIdentity;
            return newIdentity;
        }
    }
    
    /// <summary>
    /// Combines two GUIDs to create a new deterministic GUID.
    /// Uses XOR to combine the byte arrays, ensuring the result is unique per combination.
    /// </summary>
    private static Guid CombineGuids(Guid a, Guid b)
    {
        var bytesA = a.ToByteArray();
        var bytesB = b.ToByteArray();
        var result = new byte[16];
        
        for (int i = 0; i < 16; i++)
        {
            result[i] = (byte)(bytesA[i] ^ bytesB[i]);
        }
        
        return new Guid(result);
    }

    /// <summary>
    /// Gets the default local player's identity (for backwards compatibility).
    /// Uses the session GUID when no profile ID is available.
    /// </summary>
    /// <param name="displayName">The display name to use.</param>
    /// <returns>The local player's network identity.</returns>
    public static NetworkPlayerIdentity GetOrCreate(string displayName)
    {
        lock (_lock)
        {
            if (_defaultIdentity is not null)
            {
                // Update display name if it changed
                if (_defaultIdentity.DisplayName != displayName)
                {
                    _defaultIdentity = _defaultIdentity.WithDisplayName(displayName);
                }
                return _defaultIdentity;
            }

            // Create a new identity with the session GUID (unique per process)
            _defaultIdentity = NetworkPlayerIdentity.FromData(_sessionId, displayName);
            return _defaultIdentity;
        }
    }

    /// <summary>
    /// Gets the session-unique ID for this process instance.
    /// </summary>
    public static Guid SessionId => _sessionId;

    /// <summary>
    /// Gets the display name from the default identity, or null if not set.
    /// </summary>
    public static string? DisplayName
    {
        get
        {
            lock (_lock)
            {
                return _defaultIdentity?.DisplayName;
            }
        }
    }

    /// <summary>
    /// Updates the display name of the default identity.
    /// </summary>
    /// <param name="newDisplayName">The new display name.</param>
    /// <returns>True if the identity was updated, false if no identity exists.</returns>
    public static bool UpdateDisplayName(string newDisplayName)
    {
        lock (_lock)
        {
            if (_defaultIdentity is null)
            {
                return false;
            }

            _defaultIdentity = _defaultIdentity.WithDisplayName(newDisplayName);
            return true;
        }
    }

    /// <summary>
    /// Clears all cached identities. Used for testing or when logging out.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _defaultIdentity = null;
            _identityCache.Clear();
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
