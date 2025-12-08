using System;

namespace YARG.Net.Sessions;

/// <summary>
/// Represents a player in a multiplayer session.
/// This is a pure data class with no Unity or transport dependencies.
/// Can be used by both game clients and dedicated servers.
/// </summary>
public class NetworkPlayer
{
    #region Events

    /// <summary>
    /// Raised when the player's display name changes.
    /// </summary>
    public event Action<string>? NameChanged;

    /// <summary>
    /// Raised when the player's ready state changes.
    /// </summary>
    public event Action<bool>? ReadyStateChanged;

    /// <summary>
    /// Raised when the player's instrument or difficulty changes.
    /// Parameter is (instrument, difficulty).
    /// </summary>
    public event Action<int, int>? InstrumentChanged;

    /// <summary>
    /// Raised when the player's gameplay ready state changes.
    /// </summary>
    public event Action<bool>? GameplayReadyChanged;

    #endregion

    #region Identity

    private string _displayName = "Player";
    private Guid _playerId;
    private Guid _connectionId;
    private int _playerIndex;
    private bool _isHost;
    private bool _isLocal;

    /// <summary>
    /// The player's display name.
    /// </summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                NameChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// The player's unique persistent ID (survives reconnects).
    /// </summary>
    public Guid PlayerId
    {
        get => _playerId;
        set => _playerId = value;
    }

    /// <summary>
    /// The network connection ID (changes per connection).
    /// </summary>
    public Guid ConnectionId
    {
        get => _connectionId;
        set => _connectionId = value;
    }

    /// <summary>
    /// The player's index in the lobby (0-based).
    /// </summary>
    public int PlayerIndex
    {
        get => _playerIndex;
        set => _playerIndex = value;
    }

    /// <summary>
    /// Whether this player is the host.
    /// </summary>
    public bool IsHost
    {
        get => _isHost;
        set => _isHost = value;
    }

    /// <summary>
    /// Whether this player is local to this client.
    /// </summary>
    public bool IsLocal
    {
        get => _isLocal;
        set => _isLocal = value;
    }

    /// <summary>
    /// Alias for IsLocal for compatibility.
    /// </summary>
    public bool IsLocalUser => _isLocal;

    #endregion

    #region Network State

    private float _ping;

    /// <summary>
    /// The player's current ping/latency in milliseconds.
    /// </summary>
    public float Ping
    {
        get => _ping;
        set => _ping = value;
    }

    #endregion

    #region Lobby State

    private bool _isReady;
    private int _instrument;
    private int _difficulty;

    /// <summary>
    /// Whether the player is ready to start.
    /// </summary>
    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (_isReady != value)
            {
                _isReady = value;
                ReadyStateChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// The player's selected instrument.
    /// </summary>
    public int Instrument
    {
        get => _instrument;
        set
        {
            if (_instrument != value)
            {
                _instrument = value;
                InstrumentChanged?.Invoke(value, _difficulty);
            }
        }
    }

    /// <summary>
    /// The player's selected difficulty.
    /// </summary>
    public int Difficulty
    {
        get => _difficulty;
        set
        {
            if (_difficulty != value)
            {
                _difficulty = value;
                InstrumentChanged?.Invoke(_instrument, value);
            }
        }
    }

    /// <summary>
    /// Sets both instrument and difficulty at once.
    /// </summary>
    public void SetInstrumentAndDifficulty(int instrument, int difficulty)
    {
        _instrument = instrument;
        _difficulty = difficulty;
        InstrumentChanged?.Invoke(instrument, difficulty);
    }

    #endregion

    #region Gameplay State

    private int _currentScore;
    private int _currentCombo;
    private int _currentStreak;
    private bool _isStarPowerActive;
    private float _starPowerAmount;
    private int _starPowerPhrasesHit;
    private int _totalStarPowerPhrases;
    private int _notesHit;
    private int _notesMissed;
    private int _bandBonusScore;
    private int _overstrums;
    private int _hoposStrummed;
    private int _overhits;
    private int _ghostInputs;
    private int _ghostsHit;
    private int _accentsHit;
    private int _dynamicsBonus;
    private int _vocalsTicksHit;
    private int _vocalsTicksMissed;
    private float _vocalsPhraseTicksHit;
    private int _vocalsPhraseTicksTotal;
    private uint _lastGameplaySnapshotSequence;
    private bool _soloActive;
    private int _soloSequence = -1;
    private int _soloNoteCount;
    private int _soloNotesHit;
    private int _soloLastBonus;
    private int _soloTotalBonus;
    private int _sustainsHeld;
    private float _whammyValue;
    private double _lastGameplaySongTime;
    private double _lastGameplayNetworkTime;
    private float _lastGameplayLatencyMs;
    private bool _gameplayReady;
    private double _gameplayReadyServerTime;
    private bool _hasFailed;

    // Gameplay state properties (read-only to external code, set via ApplySnapshot)
    public int CurrentScore => _currentScore;
    public int CurrentCombo => _currentCombo;
    public int CurrentStreak => _currentStreak;
    public bool IsStarPowerActive => _isStarPowerActive;
    public float StarPowerAmount => _starPowerAmount;
    public int StarPowerPhrasesHit => _starPowerPhrasesHit;
    public int TotalStarPowerPhrases => _totalStarPowerPhrases;
    public int NotesHit => _notesHit;
    public int NotesMissed => _notesMissed;
    public int BandBonusScore => _bandBonusScore;
    public int Overstrums => _overstrums;
    public int HoposStrummed => _hoposStrummed;
    public int Overhits => _overhits;
    public int GhostInputs => _ghostInputs;
    public int GhostsHit => _ghostsHit;
    public int AccentsHit => _accentsHit;
    public int DynamicsBonus => _dynamicsBonus;
    public int VocalsTicksHit => _vocalsTicksHit;
    public int VocalsTicksMissed => _vocalsTicksMissed;
    public float VocalsPhraseTicksHit => _vocalsPhraseTicksHit;
    public int VocalsPhraseTicksTotal => _vocalsPhraseTicksTotal;
    public uint LastGameplaySnapshotSequence => _lastGameplaySnapshotSequence;
    public double LastGameplaySongTime => _lastGameplaySongTime;
    public double LastGameplayNetworkTime => _lastGameplayNetworkTime;
    public float LastGameplayLatencyMs => _lastGameplayLatencyMs;
    public bool SoloActive => _soloActive;
    public int SoloSequence => _soloSequence;
    public int SoloNoteCount => _soloNoteCount;
    public int SoloNotesHit => _soloNotesHit;
    public int SoloLastBonus => _soloLastBonus;
    public int SoloTotalBonus => _soloTotalBonus;
    public int SustainsHeld => _sustainsHeld;
    public float WhammyValue => _whammyValue;

    /// <summary>
    /// Whether the player is ready for gameplay to begin.
    /// </summary>
    public bool GameplayReady
    {
        get => _gameplayReady;
        set
        {
            if (_gameplayReady != value)
            {
                _gameplayReady = value;
                GameplayReadyChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Server time when gameplay ready was set.
    /// </summary>
    public double GameplayReadyServerTime
    {
        get => _gameplayReadyServerTime;
        set => _gameplayReadyServerTime = value;
    }

    /// <summary>
    /// Whether the player has failed during gameplay.
    /// </summary>
    public bool HasFailed
    {
        get => _hasFailed;
        set => _hasFailed = value;
    }

    /// <summary>
    /// Apply a gameplay snapshot received from the network.
    /// </summary>
    public void ApplySnapshot(
        uint sequence,
        int score,
        int combo,
        int streak,
        bool starPowerActive,
        float starPowerAmount,
        int starPowerPhrasesHit,
        int totalStarPowerPhrases,
        int notesHit,
        int notesMissed,
        int bandBonusScore,
        int overstrums,
        int hoposStrummed,
        int overhits,
        int ghostInputs,
        int ghostsHit,
        int accentsHit,
        int dynamicsBonus,
        int vocalsTicksHit,
        int vocalsTicksMissed,
        float vocalsPhraseTicksHit,
        int vocalsPhraseTicksTotal,
        bool soloActive,
        int soloSequence,
        int soloNoteCount,
        int soloNotesHit,
        int soloLastBonus,
        int soloTotalBonus,
        int sustainsHeld,
        float whammyValue,
        double songTime,
        double networkTime,
        float latencyMs)
    {
        // Reject stale snapshots
        if (sequence <= _lastGameplaySnapshotSequence)
        {
            return;
        }

        _lastGameplaySnapshotSequence = sequence;
        _currentScore = score;
        _currentCombo = combo;
        _currentStreak = streak;
        _isStarPowerActive = starPowerActive;
        _starPowerAmount = starPowerAmount;
        _starPowerPhrasesHit = starPowerPhrasesHit;
        _totalStarPowerPhrases = totalStarPowerPhrases;
        _notesHit = notesHit;
        _notesMissed = notesMissed;
        _bandBonusScore = bandBonusScore;
        _overstrums = overstrums;
        _hoposStrummed = hoposStrummed;
        _overhits = overhits;
        _ghostInputs = ghostInputs;
        _ghostsHit = ghostsHit;
        _accentsHit = accentsHit;
        _dynamicsBonus = dynamicsBonus;
        _vocalsTicksHit = vocalsTicksHit;
        _vocalsTicksMissed = vocalsTicksMissed;
        _vocalsPhraseTicksHit = vocalsPhraseTicksHit;
        _vocalsPhraseTicksTotal = vocalsPhraseTicksTotal;
        _soloActive = soloActive;
        _soloSequence = soloSequence;
        _soloNoteCount = soloNoteCount;
        _soloNotesHit = soloNotesHit;
        _soloLastBonus = soloLastBonus;
        _soloTotalBonus = soloTotalBonus;
        _sustainsHeld = sustainsHeld;
        _whammyValue = whammyValue;
        _lastGameplaySongTime = songTime;
        _lastGameplayNetworkTime = networkTime;
        _lastGameplayLatencyMs = latencyMs;
    }

    /// <summary>
    /// Reset gameplay state for a new song.
    /// </summary>
    public void ResetGameState()
    {
        _currentScore = 0;
        _currentCombo = 0;
        _currentStreak = 0;
        _isStarPowerActive = false;
        _starPowerAmount = 0f;
        _starPowerPhrasesHit = 0;
        _totalStarPowerPhrases = 0;
        _notesHit = 0;
        _notesMissed = 0;
        _bandBonusScore = 0;
        _overstrums = 0;
        _hoposStrummed = 0;
        _overhits = 0;
        _ghostInputs = 0;
        _ghostsHit = 0;
        _accentsHit = 0;
        _dynamicsBonus = 0;
        _vocalsTicksHit = 0;
        _vocalsTicksMissed = 0;
        _vocalsPhraseTicksHit = 0f;
        _vocalsPhraseTicksTotal = 0;
        _soloActive = false;
        _soloSequence = -1;
        _soloNoteCount = 0;
        _soloNotesHit = 0;
        _soloLastBonus = 0;
        _soloTotalBonus = 0;
        _sustainsHeld = 0;
        _whammyValue = 0f;
        _lastGameplaySnapshotSequence = 0;
        _lastGameplaySongTime = 0d;
        _lastGameplayNetworkTime = 0d;
        _lastGameplayLatencyMs = 0f;
        _hasFailed = false;
    }

    #endregion
}
