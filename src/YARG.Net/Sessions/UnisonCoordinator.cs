using System;
using System.Collections.Generic;
using YARG.Net.Packets;

namespace YARG.Net.Sessions;

/// <summary>
/// Coordinates unison star power phrases across all players.
/// Tracks which players have completed each phrase and awards bonuses when all complete.
/// Server-authoritative - only the host/server runs this.
/// </summary>
public sealed class UnisonCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<double, HashSet<string>> _phraseCompletions = new();
    private readonly HashSet<double> _awardedPhrases = new();
    
    private int _expectedPlayerCount;

    /// <summary>
    /// Tolerance for matching phrase times (in seconds).
    /// Phrases within this tolerance are considered the same phrase.
    /// </summary>
    public const double PhraseTolerance = 0.1;

    /// <summary>
    /// Gets or sets the expected number of players for unison completion.
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
    /// Records that a player has completed a unison phrase.
    /// </summary>
    /// <param name="playerKey">Unique identifier for the player (connection ID or name).</param>
    /// <param name="phraseTime">The time of the unison phrase.</param>
    /// <param name="phraseEndTime">The end time of the unison phrase (used for matching).</param>
    /// <returns>True if this completion triggered a bonus award, false otherwise.</returns>
    public bool RecordPhraseHit(string playerKey, double phraseTime, double phraseEndTime)
    {
        if (string.IsNullOrEmpty(playerKey))
        {
            return false;
        }

        lock (_gate)
        {
            // Use phraseTime as the key (could also use phraseEndTime or a combination)
            var phraseKey = NormalizePhraseTime(phraseTime);

            // Check if already awarded
            if (_awardedPhrases.Contains(phraseKey))
            {
                return false;
            }

            // Get or create completions set for this phrase
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

            // Check if all expected players have completed
            if (completions.Count >= _expectedPlayerCount && _expectedPlayerCount > 0)
            {
                _awardedPhrases.Add(phraseKey);
                UnisonBonusAwarded?.Invoke(this, new UnisonBonusEventArgs(phraseKey, completions.Count));
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets completion info for a phrase (for debugging/UI).
    /// </summary>
    public (int completed, int expected) GetPhraseStatus(double phraseTime)
    {
        lock (_gate)
        {
            var phraseKey = NormalizePhraseTime(phraseTime);
            var completed = _phraseCompletions.TryGetValue(phraseKey, out var set) ? set.Count : 0;
            return (completed, _expectedPlayerCount);
        }
    }

    /// <summary>
    /// Checks if a phrase has already been awarded.
    /// </summary>
    public bool WasAwarded(double phraseTime)
    {
        lock (_gate)
        {
            var phraseKey = NormalizePhraseTime(phraseTime);
            return _awardedPhrases.Contains(phraseKey);
        }
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
        }
    }

    /// <summary>
    /// Builds a UnisonBonusAwardPacket for the given phrase time.
    /// </summary>
    public UnisonBonusAwardPacket BuildAwardPacket(Guid lobbyId, double phraseTime)
    {
        return new UnisonBonusAwardPacket(lobbyId, phraseTime);
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
    public UnisonBonusEventArgs(double phraseTime, int playerCount)
    {
        PhraseTime = phraseTime;
        PlayerCount = playerCount;
    }

    /// <summary>
    /// The normalized time of the unison phrase.
    /// </summary>
    public double PhraseTime { get; }

    /// <summary>
    /// The number of players who completed the phrase.
    /// </summary>
    public int PlayerCount { get; }
}
