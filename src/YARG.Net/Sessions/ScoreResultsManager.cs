using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Net.Packets;

namespace YARG.Net.Sessions;

/// <summary>
/// Manages score results from all players after a song is completed.
/// Caches results so late subscribers can retrieve them after scene transitions.
/// </summary>
public sealed class ScoreResultsManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PlayerScoreResult> _results = new();

    /// <summary>
    /// Gets the number of cached results.
    /// </summary>
    public int ResultCount
    {
        get
        {
            lock (_gate)
            {
                return _results.Count;
            }
        }
    }

    /// <summary>
    /// Records a player's score result.
    /// </summary>
    public void RecordResult(string playerName, bool isHighScore, bool isFullCombo, 
        int score, int maxCombo, int notesHit, int notesMissed)
    {
        if (string.IsNullOrEmpty(playerName))
        {
            return;
        }

        var result = new PlayerScoreResult(
            playerName, isHighScore, isFullCombo, 
            score, maxCombo, notesHit, notesMissed);

        lock (_gate)
        {
            _results[playerName] = result;
        }

        ResultReceived?.Invoke(this, new ScoreResultEventArgs(result));
    }

    /// <summary>
    /// Records a result from a ScoreResultsPacket.
    /// </summary>
    public void RecordResult(ScoreResultsPacket packet)
    {
        if (packet == null)
        {
            return;
        }

        RecordResult(
            packet.PlayerName,
            packet.IsHighScore,
            packet.IsFullCombo,
            packet.Score,
            packet.MaxCombo,
            packet.NotesHit,
            packet.NotesMissed);
    }

    /// <summary>
    /// Tries to get a specific player's result.
    /// </summary>
    public bool TryGetResult(string playerName, out PlayerScoreResult? result)
    {
        if (string.IsNullOrEmpty(playerName))
        {
            result = null;
            return false;
        }

        lock (_gate)
        {
            return _results.TryGetValue(playerName, out result);
        }
    }

    /// <summary>
    /// Gets all cached results.
    /// </summary>
    public IReadOnlyList<PlayerScoreResult> GetAllResults()
    {
        lock (_gate)
        {
            return _results.Values.ToList();
        }
    }

    /// <summary>
    /// Gets all results as a dictionary (for compatibility with existing code).
    /// </summary>
    public Dictionary<string, PlayerScoreResult> GetResultsDictionary()
    {
        lock (_gate)
        {
            return new Dictionary<string, PlayerScoreResult>(_results);
        }
    }

    /// <summary>
    /// Gets results sorted by score (descending).
    /// </summary>
    public IReadOnlyList<PlayerScoreResult> GetResultsByScore()
    {
        lock (_gate)
        {
            return _results.Values.OrderByDescending(r => r.Score).ToList();
        }
    }

    /// <summary>
    /// Clears all cached results. Call when starting new gameplay.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _results.Clear();
        }
    }

    /// <summary>
    /// Builds a ScoreResultsPacket from local data.
    /// </summary>
    public ScoreResultsPacket BuildPacket(Guid sessionId, string playerName, 
        bool isHighScore, bool isFullCombo, int score, int maxCombo, int notesHit, int notesMissed)
    {
        return new ScoreResultsPacket(
            sessionId, playerName, isHighScore, isFullCombo,
            score, maxCombo, notesHit, notesMissed);
    }

    /// <summary>
    /// Raised when a new result is received.
    /// </summary>
    public event EventHandler<ScoreResultEventArgs>? ResultReceived;
}

/// <summary>
/// Immutable record of a player's score result.
/// </summary>
public sealed record PlayerScoreResult(
    string PlayerName,
    bool IsHighScore,
    bool IsFullCombo,
    int Score,
    int MaxCombo,
    int NotesHit,
    int NotesMissed)
{
    /// <summary>
    /// Total notes for this player.
    /// </summary>
    public int TotalNotes => NotesHit + NotesMissed;

    /// <summary>
    /// Accuracy percentage (0-100).
    /// </summary>
    public float AccuracyPercent => TotalNotes > 0 ? (NotesHit * 100f / TotalNotes) : 0f;
}

/// <summary>
/// Event args for score result events.
/// </summary>
public sealed class ScoreResultEventArgs : EventArgs
{
    public ScoreResultEventArgs(PlayerScoreResult result)
    {
        Result = result;
    }

    public PlayerScoreResult Result { get; }
}
