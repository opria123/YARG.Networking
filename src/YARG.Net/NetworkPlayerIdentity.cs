using System;

namespace YARG.Net;

/// <summary>
/// Represents a player's network identity with a stable unique ID and display name.
/// The PlayerId is a persistent GUID that uniquely identifies a player across sessions,
/// while DisplayName is the human-readable name shown in the UI.
/// </summary>
public sealed class NetworkPlayerIdentity : IEquatable<NetworkPlayerIdentity>
{
    /// <summary>
    /// Creates a new player identity with the specified ID and display name.
    /// </summary>
    /// <param name="playerId">The unique player identifier (should be persisted).</param>
    /// <param name="displayName">The human-readable display name.</param>
    public NetworkPlayerIdentity(Guid playerId, string displayName)
    {
        if (playerId == Guid.Empty)
        {
            throw new ArgumentException("Player ID cannot be empty.", nameof(playerId));
        }

        PlayerId = playerId;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    /// The unique, persistent identifier for this player.
    /// This should be stored locally and reused across sessions.
    /// </summary>
    public Guid PlayerId { get; }

    /// <summary>
    /// The human-readable display name shown in the UI.
    /// This can be changed by the player.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Creates a new identity with a randomly generated player ID.
    /// </summary>
    /// <param name="displayName">The display name for the player.</param>
    /// <returns>A new identity with a unique player ID.</returns>
    public static NetworkPlayerIdentity CreateNew(string displayName)
    {
        return new NetworkPlayerIdentity(Guid.NewGuid(), displayName);
    }

    /// <summary>
    /// Creates an identity from serialized data.
    /// </summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <param name="displayName">The player's display name.</param>
    /// <returns>A new identity instance.</returns>
    public static NetworkPlayerIdentity FromData(Guid playerId, string displayName)
    {
        return new NetworkPlayerIdentity(playerId, displayName);
    }

    /// <summary>
    /// Returns a new identity with an updated display name but the same player ID.
    /// </summary>
    /// <param name="newDisplayName">The new display name.</param>
    /// <returns>A new identity instance with the updated name.</returns>
    public NetworkPlayerIdentity WithDisplayName(string newDisplayName)
    {
        return new NetworkPlayerIdentity(PlayerId, newDisplayName);
    }

    public override string ToString()
    {
        return $"{DisplayName} ({PlayerId:N})";
    }

    public override int GetHashCode()
    {
        return PlayerId.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as NetworkPlayerIdentity);
    }

    public bool Equals(NetworkPlayerIdentity? other)
    {
        if (other is null)
        {
            return false;
        }

        return PlayerId == other.PlayerId;
    }

    public static bool operator ==(NetworkPlayerIdentity? left, NetworkPlayerIdentity? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(NetworkPlayerIdentity? left, NetworkPlayerIdentity? right)
    {
        return !(left == right);
    }
}
