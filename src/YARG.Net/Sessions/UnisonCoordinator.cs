using System;
using System.Collections.Generic;
using YARG.Net.Packets;

namespace YARG.Net.Sessions;

/// <summary>
/// Coordinates unison star power phrases across all players, per-band.
/// Tracks which players have completed each phrase and awards bonuses when all players
/// in the same band complete the phrase. Server-authoritative - only the host/server runs this.
/// </summary>
public sealed class UnisonCoordinator
{
    private readonly object _gate = new();
    
    // Key is (bandId, normalizedPhraseTime), value is set of player keys who completed
    private readonly Dictionary<(int bandId, double phraseTime), HashSet<string>> _phraseCompletions = new();
    private readonly HashSet<(int bandId, double phraseTime)> _awardedPhrases = new();
    
    // Expected player count per band
    private readonly Dictionary<int, int> _expectedPlayerCountByBand = new();
    
    // Legacy global player count for backwards compatibility
    private int _expectedPlayerCount;

    /// <summary>
    /// Tolerance for matching phrase times (in seconds).
    /// Phrases within this tolerance are considered the same phrase.
    /// </summary>
    public const double PhraseTolerance = 0.1;

    /// <summary>
    /// Gets or sets the expected number of players for unison completion (legacy global).
    /// For per-band tracking, use SetBandPlayerCount instead.
    /// </summary>
    public int ExpectedPlayerCount
    {
        get
        {
            lock (_gate)
            {
                return _expectedPlayerCount;
            }
        }
        set
        {
            lock (_gate)
            {
                _expectedPlayerCount = Math.Max(0, value);
            }
        }
    }
    
    /// <summary>
    /// Sets the expected player count for a specific band.
    /// </summary>
    /// <param name="bandId">The band ID.</param>
    /// <param name="playerCount">The number of players in this band.</param>
    public void SetBandPlayerCount(int bandId, int playerCount)
    {
        lock (_gate)
        {
            _expectedPlayerCountByBand[bandId] = Math.Max(0, playerCount);
        }
    }
    
    /// <summary>
    /// Gets the expected player count for a specific band.
    /// </summary>
    public int GetBandPlayerCount(int bandId)
    {
        lock (_gate)
        {
            return _expectedPlayerCountByBand.TryGetValue(bandId, out var count) ? count : _expectedPlayerCount;
        }
    }
    
    /// <summary>
    /// Clears all band player counts.
    /// </summary>
    public void ClearBandPlayerCounts()
    {
        lock (_gate)
        {
            _expectedPlayerCountByBand.Clear();
        }
    }

    /// <summary>
    /// Records that a player has completed a unison phrase (per-band).
    /// </summary>
    /// <param name="playerKey">Unique identifier for the player (connection ID or name).</param>
    /// <param name="bandId">The band the player belongs to.</param>
    /// <param name="phraseTime">The time of the unison phrase.</param>
    /// <param name="phraseEndTime">The end time of the unison phrase (used for matching).</param>
    /// <returns>True if this completion triggered a bonus award, false otherwise.</returns>
    public bool RecordPhraseHit(string playerKey, int bandId, double phraseTime, double phraseEndTime)
    {
        if (string.IsNullOrEmpty(playerKey))
        {
            return false;
        }

        lock (_gate)
        {
            var normalizedTime = NormalizePhraseTime(phraseTime);
            var phraseKey = (bandId, normalizedTime);

            // Check if already awarded for this band
            if (_awardedPhrases.Contains(phraseKey))
            {
                return false;
            }

            // Get or create completions set for this band's phrase
            if (!_phraseCompletions.TryGetValue(phraseKey, out var completions))
            {
                completions = new HashSet<string>(StringComparer.Ordinal);
                _phraseCompletions[phraseKey] = completions;
            }

            // Check if this player already hit this phrase
            if (completions.Contains(playerKey))
            {
                return false;
            }

            completions.Add(playerKey);

            // Get expected count for this band (falls back to global if not set)
            var expectedCount = _expectedPlayerCountByBand.TryGetValue(bandId, out var bandCount) 
                ? bandCount 
                : _expectedPlayerCount;

            // Check if all expected players in this band have completed
            if (completions.Count >= expectedCount && expectedCount > 0)
            {
                _awardedPhrases.Add(phraseKey);
                UnisonBonusAwarded?.Invoke(this, new UnisonBonusEventArgs(normalizedTime, completions.Count, bandId));
                return true;
            }

            return false;
        }
    }
    
    /// <summary>
    /// Records that a player has completed a unison phrase (legacy global, bandId=0).
    /// </summary>
    /// <param name="playerKey">Unique identifier for the player (connection ID or name).</param>
    /// <param name="phraseTime">The time of the unison phrase.</param>
    /// <param name="phraseEndTime">The end time of the unison phrase (used for matching).</param>
    /// <returns>True if this completion triggered a bonus award, false otherwise.</returns>
    public bool RecordPhraseHit(string playerKey, double phraseTime, double phraseEndTime)
    {
        // Legacy overload uses bandId=0 for backwards compatibility
        return RecordPhraseHit(playerKey, 0, phraseTime, phraseEndTime);
    }

    /// <summary>
    /// Gets completion info for a phrase in a specific band (for debugging/UI).
    /// </summary>
    public (int completed, int expected) GetPhraseStatus(int bandId, double phraseTime)
    {
        lock (_gate)
        {
            var phraseKey = (bandId, NormalizePhraseTime(phraseTime));
            var completed = _phraseCompletions.TryGetValue(phraseKey, out var set) ? set.Count : 0;
            var expected = _expectedPlayerCountByBand.TryGetValue(bandId, out var count) ? count : _expectedPlayerCount;
            return (completed, expected);
        }
    }
    
    /// <summary>
    /// Gets completion info for a phrase (legacy global, bandId=0).
    /// </summary>
    public (int completed, int expected) GetPhraseStatus(double phraseTime)
    {
        return GetPhraseStatus(0, phraseTime);
    }

    /// <summary>
    /// Checks if a phrase has already been awarded for a specific band.
    /// </summary>
    public bool WasAwarded(int bandId, double phraseTime)
    {
        lock (_gate)
        {
            var phraseKey = (bandId, NormalizePhraseTime(phraseTime));
            return _awardedPhrases.Contains(phraseKey);
        }
    }
    
    /// <summary>
    /// Checks if a phrase has already been awarded (legacy global, bandId=0).
    /// </summary>
    public bool WasAwarded(double phraseTime)
    {
        return WasAwarded(0, phraseTime);
    }

    /// <summary>
    /// Resets all tracking state. Call when starting a new song.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _phraseCompletions.Clear();
            _awardedPhrases.Clear();
            // Note: We keep _expectedPlayerCountByBand as it's set at game start
        }
    }
    
    /// <summary>
    /// Fully resets all state including band player counts. Call when leaving a lobby.
    /// </summary>
    public void FullReset()
    {
        lock (_gate)
        {
            _phraseCompletions.Clear();
            _awardedPhrases.Clear();
            _expectedPlayerCountByBand.Clear();
            _expectedPlayerCount = 0;
        }
    }

    /// <summary>
    /// Builds a UnisonBonusAwardPacket for the given phrase time and band.
    /// </summary>
    public UnisonBonusAwardPacket BuildAwardPacket(Guid lobbyId, int bandId, double phraseTime)
    {
        return new UnisonBonusAwardPacket(lobbyId, bandId, phraseTime);
    }

    /// <summary>
    /// Normalizes phrase time to a consistent key (rounds to tolerance).
    /// </summary>
    private static double NormalizePhraseTime(double time)
    {
        // Round to nearest tolerance interval
        return Math.Round(time / PhraseTolerance) * PhraseTolerance;
    }

    /// <summary>
    /// Raised when a unison bonus should be awarded (all players completed the phrase).
    /// </summary>
    public event EventHandler<UnisonBonusEventArgs>? UnisonBonusAwarded;
}

/// <summary>
/// Event args for unison bonus events.
/// </summary>
public sealed class UnisonBonusEventArgs : EventArgs
{
    public UnisonBonusEventArgs(double phraseTime, int playerCount, int bandId = 0)
    {
        PhraseTime = phraseTime;
        PlayerCount = playerCount;
        BandId = bandId;
    }

    /// <summary>
    /// The normalized time of the unison phrase.
    /// </summary>
    public double PhraseTime { get; }

    /// <summary>
    /// The number of players who completed the phrase.
    /// </summary>
    public int PlayerCount { get; }
    
    /// <summary>
    /// The band ID that this unison bonus applies to.
    /// </summary>
    public int BandId { get; }
}
