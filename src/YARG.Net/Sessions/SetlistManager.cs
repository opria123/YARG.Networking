using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Net.Packets;

namespace YARG.Net.Sessions;

/// <summary>
/// Manages the setlist of songs for a multiplayer session.
/// This is server-authoritative - the host controls the setlist.
/// </summary>
public sealed class SetlistManager
{
    private readonly object _gate = new();
    private readonly List<SetlistEntry> _songs = new();
    private readonly int _maxSongs;

    /// <summary>
    /// Creates a new SetlistManager with the specified maximum number of songs.
    /// </summary>
    /// <param name="maxSongs">Maximum songs allowed in the setlist. Default is 100.</param>
    public SetlistManager(int maxSongs = 100)
    {
        if (maxSongs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSongs), "Max songs must be greater than zero.");
        }
        _maxSongs = maxSongs;
    }

    /// <summary>
    /// Gets the current number of songs in the setlist.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _songs.Count;
            }
        }
    }

    /// <summary>
    /// Gets whether the setlist is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _songs.Count == 0;
            }
        }
    }

    /// <summary>
    /// Gets the song hashes in the setlist, in order.
    /// </summary>
    public IReadOnlyList<string> SongHashes
    {
        get
        {
            lock (_gate)
            {
                return _songs.Select(s => s.SongHash).ToList();
            }
        }
    }

    /// <summary>
    /// Gets a copy of all setlist entries.
    /// </summary>
    public IReadOnlyList<SetlistEntry> GetAllEntries()
    {
        lock (_gate)
        {
            return _songs.ToList();
        }
    }

    /// <summary>
    /// Checks if a song is in the setlist.
    /// </summary>
    public bool Contains(string songHash)
    {
        if (string.IsNullOrEmpty(songHash))
        {
            return false;
        }

        lock (_gate)
        {
            return _songs.Any(s => string.Equals(s.SongHash, songHash, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Attempts to add a song to the setlist.
    /// </summary>
    /// <returns>True if the song was added, false if it already exists or setlist is full.</returns>
    public bool TryAdd(string songHash, string songName, string songArtist, string addedByPlayerName, out SetlistEntry? entry)
    {
        if (string.IsNullOrEmpty(songHash))
        {
            entry = null;
            return false;
        }

        lock (_gate)
        {
            if (_songs.Count >= _maxSongs)
            {
                entry = null;
                return false;
            }

            if (_songs.Any(s => string.Equals(s.SongHash, songHash, StringComparison.OrdinalIgnoreCase)))
            {
                entry = null;
                return false;
            }

            entry = new SetlistEntry(songHash, songName ?? string.Empty, songArtist ?? string.Empty, addedByPlayerName ?? string.Empty);
            _songs.Add(entry);
            SongAdded?.Invoke(this, new SetlistSongEventArgs(entry));
            return true;
        }
    }

    /// <summary>
    /// Attempts to remove a song from the setlist.
    /// </summary>
    /// <returns>True if the song was removed, false if it wasn't in the setlist.</returns>
    public bool TryRemove(string songHash, out SetlistEntry? removedEntry)
    {
        if (string.IsNullOrEmpty(songHash))
        {
            removedEntry = null;
            return false;
        }

        lock (_gate)
        {
            var index = _songs.FindIndex(s => string.Equals(s.SongHash, songHash, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                removedEntry = null;
                return false;
            }

            removedEntry = _songs[index];
            _songs.RemoveAt(index);
            SongRemoved?.Invoke(this, new SetlistSongEventArgs(removedEntry));
            return true;
        }
    }

    /// <summary>
    /// Removes and returns the first song in the setlist.
    /// Used when advancing to the next song in the show.
    /// </summary>
    /// <returns>The removed song entry, or null if the setlist is empty.</returns>
    public SetlistEntry? PopFirst()
    {
        lock (_gate)
        {
            if (_songs.Count == 0)
            {
                return null;
            }

            var entry = _songs[0];
            _songs.RemoveAt(0);
            SongRemoved?.Invoke(this, new SetlistSongEventArgs(entry));
            return entry;
        }
    }

    /// <summary>
    /// Gets the first song in the setlist without removing it.
    /// </summary>
    public SetlistEntry? PeekFirst()
    {
        lock (_gate)
        {
            return _songs.Count > 0 ? _songs[0] : null;
        }
    }

    /// <summary>
    /// Clears all songs from the setlist.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_songs.Count == 0)
            {
                return;
            }

            _songs.Clear();
            SetlistCleared?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Replaces the entire setlist with a new list of entries.
    /// Used when syncing state to a newly joined client.
    /// </summary>
    public void ReplaceAll(IEnumerable<SetlistEntry> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        lock (_gate)
        {
            _songs.Clear();
            foreach (var entry in entries.Take(_maxSongs))
            {
                _songs.Add(entry);
            }
            SetlistSynced?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Replaces the entire setlist with a list of song hashes.
    /// Entries will have minimal info (just the hash).
    /// </summary>
    public void ReplaceAllFromHashes(IEnumerable<string> songHashes)
    {
        if (songHashes is null)
        {
            throw new ArgumentNullException(nameof(songHashes));
        }

        lock (_gate)
        {
            _songs.Clear();
            foreach (var hash in songHashes.Take(_maxSongs))
            {
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    _songs.Add(new SetlistEntry(hash, string.Empty, string.Empty, string.Empty));
                }
            }
            SetlistSynced?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Serializes the setlist as a pipe-delimited string of song hashes.
    /// </summary>
    public string SerializeToString()
    {
        lock (_gate)
        {
            return string.Join("|", _songs.Select(s => s.SongHash));
        }
    }

    /// <summary>
    /// Deserializes a pipe-delimited string of song hashes and replaces the setlist.
    /// </summary>
    public void DeserializeFromString(string serialized)
    {
        lock (_gate)
        {
            _songs.Clear();
            if (!string.IsNullOrEmpty(serialized))
            {
                var hashes = serialized.Split('|');
                foreach (var hash in hashes.Take(_maxSongs))
                {
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        _songs.Add(new SetlistEntry(hash, string.Empty, string.Empty, string.Empty));
                    }
                }
            }
            SetlistSynced?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Builds a SetlistSyncPacket for sending to clients.
    /// </summary>
    public SetlistSyncPacket BuildSyncPacket(Guid lobbyId)
    {
        lock (_gate)
        {
            return new SetlistSyncPacket(lobbyId, _songs.ToList());
        }
    }

    /// <summary>
    /// Builds a SetlistStartPacket for starting the show.
    /// </summary>
    public SetlistStartPacket BuildStartPacket(Guid lobbyId)
    {
        lock (_gate)
        {
            return new SetlistStartPacket(lobbyId, _songs.Select(s => s.SongHash).ToList());
        }
    }

    /// <summary>
    /// Raised when a song is added to the setlist.
    /// </summary>
    public event EventHandler<SetlistSongEventArgs>? SongAdded;

    /// <summary>
    /// Raised when a song is removed from the setlist.
    /// </summary>
    public event EventHandler<SetlistSongEventArgs>? SongRemoved;

    /// <summary>
    /// Raised when the setlist is cleared.
    /// </summary>
    public event EventHandler? SetlistCleared;

    /// <summary>
    /// Raised when the setlist is synced (replaced entirely).
    /// </summary>
    public event EventHandler? SetlistSynced;
}

/// <summary>
/// Event args for setlist song events.
/// </summary>
public sealed class SetlistSongEventArgs : EventArgs
{
    public SetlistSongEventArgs(SetlistEntry entry)
    {
        Entry = entry;
    }

    public SetlistEntry Entry { get; }
}
