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
    
    // Lobby State (10-19)
    LobbyState = 10,
    LobbyInvite = 11,
    LobbyReadyState = 12,
    PlayerJoined = 13,
    PlayerLeft = 14,
    NavigateToMenu = 15,
    AllPlayersReady = 16,
    
    // Song Selection / Setlist (20-29)
    SongSelection = 20,
    SetlistAdd = 21,
    SetlistRemove = 22,
    SetlistSync = 23,
    SetlistStart = 24,
    SongLibraryChunk = 25,
    SharedSongsChunk = 26,
    ClearSharedSongs = 27,
    
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
    
    // Replay Sync (40-49)
    ReplaySyncRequest = 40,
    ReplaySyncData = 41,
    ReplaySyncComplete = 42,
    
    // Score / Results (50-59)
    ScoreScreenAdvance = 50,
    ScoreResults = 51,
    
    // Unison / Band Mechanics (60-69)
    UnisonPhraseHit = 60,
    UnisonBonusAward = 61,
}
