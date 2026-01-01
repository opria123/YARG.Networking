namespace YARG.Net.Packets;

public enum PacketType : byte
{
    // Connection / Handshake (1-9)
    HandshakeRequest = 1,
    HandshakeResponse = 2,
    Heartbeat = 3,
    HostDisconnect = 4,
    AuthRequest = 5,
    AuthResponse = 6,
    IdentityRequest = 7,  // Host requests player identity from client (fallback if HandshakeRequest is lost)
    
    // Lobby State (10-19)
    LobbyState = 10,
    LobbyInvite = 11,
    LobbyReadyState = 12,
    PlayerJoined = 13,
    PlayerLeft = 14,
    NavigateToMenu = 15,
    AllPlayersReady = 16,
    HostChanged = 17, // Dedicated server broadcasts new host player
    NavigationRequest = 18, // Client (designated host) requests server to navigate all players
    
    // Song Selection / Setlist (20-29)
    SongSelection = 20,
    SetlistAdd = 21,
    SetlistRemove = 22,
    SetlistSync = 23,
    SetlistStart = 24,
    SongLibraryChunk = 25,
    SharedSongsChunk = 26,
    ClearSharedSongs = 27,
    SetlistStartRequest = 28, // Client (designated host) requests server to start the show
    
    // Gameplay (30-39)
    GameplayCountdown = 30,
    GameplayInputFrame = 31,
    GameplayState = 32,
    GameplayStart = 33,
    GameplayTimeSync = 34,
    GameplayPause = 35,
    GameplayEnd = 36,
    GameplayRestart = 37,
    PlayerLeftGameplay = 38,
    QuitToLibrary = 39,
    GameplayLoadReady = 100,  // Client signals loading complete
    GameplayAllLoadReady = 101, // Host broadcasts when all players finished loading
    
    // Replay Sync (40-49)
    ReplaySyncRequest = 40,
    ReplaySyncData = 41,
    ReplaySyncComplete = 42,
    
    // Score / Results (50-59)
    ScoreScreenAdvance = 50,
    ScoreResults = 51,
    ScoreScreenAdvanceRequest = 52, // Client (designated host) requests server to advance from score screen
    
    // Unison / Band Mechanics (60-69)
    UnisonPhraseHit = 60,
    UnisonBonusAward = 61,
    
    // Band System (70-79)
    BandAssignment = 70,
    BandScoreUpdate = 71,
    BandLeaderboard = 72,
    BandNameChange = 73,
    BandNameChangeRequest = 74, // Client requests host to regenerate a band name
    BandFailed = 75, // Band failure notification for spectate mode
    
    // Track Ordering (80-89)
    TrackOrder = 80,
    TrackReorderRequest = 81,
    
    // Session Settings (90-99)
    SessionPresetSync = 90,
    GameplaySettingsSync = 91,
    PlayerPresetSync = 92, // Sync player visual presets (camera, highway, colors) when EnablePresetSync is true
    
    // Late Join (110-119)
    LateJoinState = 110,           // Host -> Client: Current session state for late join check
    LateJoinSongCheckResponse = 111, // Client -> Host: Which setlist songs the late joiner has
    LateJoinAction = 112,          // Host -> Client: Decision on what late joiner should do
    SetlistAbort = 113,            // Host -> All: Setlist aborted due to late joiner missing songs
    
    // Vote-to-Kick (120-129)
    VoteKickStart = 120,           // Broadcast: New vote session started
    VoteKickCast = 121,            // Client -> Host: Player casting a vote
    VoteKickUpdate = 122,          // Host -> All: Vote count update
    VoteKickResult = 123,          // Host -> All: Final result of vote
}
