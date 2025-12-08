using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Net.Packets;

namespace YARG.Net.Sessions;

/// <summary>
/// Manages the shared song library for a multiplayer lobby.
/// Computes the intersection of all connected players' song libraries.
/// This is used to filter the music library UI to only show songs everyone has.
/// </summary>
public sealed class SharedSongLibraryManager
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<byte[]>> _playerLibraries = new(16);
    private readonly HashSet<Guid> _pendingSyncPlayers = new();
    private readonly ByteArrayComparer _comparer = new();
    
    private HashSet<byte[]>? _sharedHashes;
    private bool _syncComplete = true;

    /// <summary>
    /// Size of a song hash in bytes (SHA-1 = 20 bytes).
    /// </summary>
    public const int HashSize = 20;

    /// <summary>
    /// Maximum hashes to send in a single chunk packet.
    /// </summary>
    public const int HashesPerChunk = 2048;

    /// <summary>
    /// Gets whether all players have completed uploading their song libraries.
    /// </summary>
    public bool IsSyncComplete
    {
        get
        {
            lock (_gate)
            {
                return _syncComplete;
            }
        }
    }

    /// <summary>
    /// Gets the number of shared songs (songs that all players have).
    /// </summary>
    public int SharedSongCount
    {
        get
        {
            lock (_gate)
            {
                return _sharedHashes?.Count ?? 0;
            }
        }
    }

    /// <summary>
    /// Gets the number of players who have uploaded their library.
    /// </summary>
    public int PlayerCount
    {
        get
        {
            lock (_gate)
            {
                return _playerLibraries.Count;
            }
        }
    }

    /// <summary>
    /// Marks a player as pending sync (they connected but haven't finished uploading).
    /// </summary>
    public void MarkPlayerPending(Guid sessionId)
    {
        lock (_gate)
        {
            _pendingSyncPlayers.Add(sessionId);
            UpdateSyncState();
        }
    }

    /// <summary>
    /// Clears a player's pending state and their library data.
    /// Called when a player starts uploading (first chunk received).
    /// </summary>
    public void BeginPlayerUpload(Guid sessionId)
    {
        lock (_gate)
        {
            // Clear any existing library for this player
            _playerLibraries[sessionId] = new HashSet<byte[]>(_comparer);
        }
    }

    /// <summary>
    /// Adds hashes from a chunk to a player's library.
    /// </summary>
    /// <param name="sessionId">The player's session ID.</param>
    /// <param name="hashData">Raw hash data (concatenated 20-byte hashes).</param>
    /// <param name="isFinalChunk">Whether this is the last chunk for this player.</param>
    public void AddPlayerHashes(Guid sessionId, ReadOnlySpan<byte> hashData, bool isFinalChunk)
    {
        lock (_gate)
        {
            if (!_playerLibraries.TryGetValue(sessionId, out var library))
            {
                library = new HashSet<byte[]>(_comparer);
                _playerLibraries[sessionId] = library;
            }

            // Parse hashes from the data
            for (int i = 0; i + HashSize <= hashData.Length; i += HashSize)
            {
                var hash = hashData.Slice(i, HashSize).ToArray();
                library.Add(hash);
            }

            if (isFinalChunk)
            {
                _pendingSyncPlayers.Remove(sessionId);
                RecalculateSharedSongs();
                UpdateSyncState();
            }
        }
    }

    /// <summary>
    /// Removes a player's library (e.g., when they disconnect).
    /// </summary>
    public void RemovePlayer(Guid sessionId)
    {
        lock (_gate)
        {
            _playerLibraries.Remove(sessionId);
            _pendingSyncPlayers.Remove(sessionId);
            RecalculateSharedSongs();
            UpdateSyncState();
        }
    }

    /// <summary>
    /// Checks if a song hash is in the shared library.
    /// </summary>
    public bool IsShared(byte[] hash)
    {
        if (hash == null || hash.Length != HashSize)
        {
            return false;
        }

        lock (_gate)
        {
            return _sharedHashes?.Contains(hash) ?? false;
        }
    }

    /// <summary>
    /// Gets all shared hashes as raw byte arrays.
    /// </summary>
    public IReadOnlyList<byte[]> GetSharedHashes()
    {
        lock (_gate)
        {
            if (_sharedHashes == null || _sharedHashes.Count == 0)
            {
                return Array.Empty<byte[]>();
            }
            return _sharedHashes.ToList();
        }
    }

    /// <summary>
    /// Clears all player libraries and shared songs.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _playerLibraries.Clear();
            _pendingSyncPlayers.Clear();
            _sharedHashes = null;
            _syncComplete = true;
            SharedSongsCleared?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Builds chunks of shared song hashes for sending to clients.
    /// </summary>
    public IReadOnlyList<SharedSongsChunkPacket> BuildSharedSongsChunks(Guid lobbyId)
    {
        lock (_gate)
        {
            var packets = new List<SharedSongsChunkPacket>();

            if (_sharedHashes == null || _sharedHashes.Count == 0)
            {
                // Send a single empty final chunk
                packets.Add(new SharedSongsChunkPacket(lobbyId, Array.Empty<byte>(), true, true));
                return packets;
            }

            var allHashes = _sharedHashes.ToArray();
            int offset = 0;

            while (offset < allHashes.Length)
            {
                int remaining = allHashes.Length - offset;
                int chunkCount = Math.Min(remaining, HashesPerChunk);
                
                var chunkData = new byte[chunkCount * HashSize];
                for (int i = 0; i < chunkCount; i++)
                {
                    Buffer.BlockCopy(allHashes[offset + i], 0, chunkData, i * HashSize, HashSize);
                }

                bool isFirst = offset == 0;
                bool isFinal = offset + chunkCount >= allHashes.Length;
                
                packets.Add(new SharedSongsChunkPacket(lobbyId, chunkData, isFirst, isFinal));
                offset += chunkCount;
            }

            return packets;
        }
    }

    private void RecalculateSharedSongs()
    {
        // Must be called under lock
        if (_playerLibraries.Count == 0)
        {
            _sharedHashes = null;
            SharedSongsChanged?.Invoke(this, new SharedSongsChangedEventArgs(0));
            return;
        }

        // Start with the first player's library
        HashSet<byte[]>? intersection = null;

        foreach (var library in _playerLibraries.Values)
        {
            if (intersection == null)
            {
                intersection = new HashSet<byte[]>(library, _comparer);
            }
            else
            {
                intersection.IntersectWith(library);
            }
        }

        _sharedHashes = intersection;
        SharedSongsChanged?.Invoke(this, new SharedSongsChangedEventArgs(_sharedHashes?.Count ?? 0));
    }

    private void UpdateSyncState()
    {
        // Must be called under lock
        bool wasComplete = _syncComplete;
        _syncComplete = _pendingSyncPlayers.Count == 0;

        if (wasComplete != _syncComplete)
        {
            SyncStateChanged?.Invoke(this, new SyncStateChangedEventArgs(_syncComplete));
        }
    }

    /// <summary>
    /// Raised when the shared songs are recalculated.
    /// </summary>
    public event EventHandler<SharedSongsChangedEventArgs>? SharedSongsChanged;

    /// <summary>
    /// Raised when the sync state changes (all players synced or not).
    /// </summary>
    public event EventHandler<SyncStateChangedEventArgs>? SyncStateChanged;

    /// <summary>
    /// Raised when shared songs are cleared.
    /// </summary>
    public event EventHandler? SharedSongsCleared;

    /// <summary>
    /// Comparer for byte arrays (for HashSet).
    /// </summary>
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            if (x.Length != y.Length) return false;
            
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i]) return false;
            }
            return true;
        }

        public int GetHashCode(byte[] obj)
        {
            if (obj == null || obj.Length < 4) return 0;
            // Use first 4 bytes as hash code (good enough for distribution)
            return BitConverter.ToInt32(obj, 0);
        }
    }
}

/// <summary>
/// Event args for shared songs changed event.
/// </summary>
public sealed class SharedSongsChangedEventArgs : EventArgs
{
    public SharedSongsChangedEventArgs(int count)
    {
        SharedSongCount = count;
    }

    public int SharedSongCount { get; }
}

/// <summary>
/// Event args for sync state changed event.
/// </summary>
public sealed class SyncStateChangedEventArgs : EventArgs
{
    public SyncStateChangedEventArgs(bool isComplete)
    {
        IsComplete = isComplete;
    }

    public bool IsComplete { get; }
}
