using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace YARG.Net.Directory;

/// <summary>
/// Core discovery protocol constants and packet building/parsing logic.
/// Transport-agnostic - can be used with LiteNetLib, raw UDP, or other transports.
/// </summary>
public static class DiscoveryProtocol
{
    /// <summary>
    /// Protocol handshake identifier: "YARGNET!" in hex.
    /// </summary>
    public const long HANDSHAKE = 0x594152474E455421;
    
    /// <summary>
    /// Discovery request message type.
    /// </summary>
    public const byte REQUEST = 0x01;
    
    /// <summary>
    /// Discovery response message type.
    /// </summary>
    public const byte RESPONSE = 0x02;
    
    /// <summary>
    /// Minimum packet size for a valid discovery message.
    /// </summary>
    public const int MIN_PACKET_SIZE = 9; // 8 bytes handshake + 1 byte type
    
    /// <summary>
    /// Builds a discovery request packet.
    /// </summary>
    public static byte[] BuildRequestPacket()
    {
        var buffer = new byte[MIN_PACKET_SIZE];
        WriteHandshakeAndType(buffer, REQUEST);
        return buffer;
    }
    
    /// <summary>
    /// Validates and parses the header of a discovery packet.
    /// </summary>
    /// <param name="data">The packet data.</param>
    /// <param name="messageType">The parsed message type (REQUEST or RESPONSE).</param>
    /// <returns>True if this is a valid discovery packet, false otherwise.</returns>
    public static bool TryParseHeader(ReadOnlySpan<byte> data, out byte messageType)
    {
        messageType = 0;
        
        if (data.Length < MIN_PACKET_SIZE)
            return false;
            
        // Read handshake (big-endian)
        long handshake = ((long)data[0] << 56) |
                        ((long)data[1] << 48) |
                        ((long)data[2] << 40) |
                        ((long)data[3] << 32) |
                        ((long)data[4] << 24) |
                        ((long)data[5] << 16) |
                        ((long)data[6] << 8) |
                        data[7];
        
        if (handshake != HANDSHAKE)
            return false;
            
        messageType = data[8];
        return messageType == REQUEST || messageType == RESPONSE;
    }
    
    /// <summary>
    /// Checks if this is a discovery request packet.
    /// </summary>
    public static bool IsRequest(ReadOnlySpan<byte> data)
    {
        return TryParseHeader(data, out var type) && type == REQUEST;
    }
    
    /// <summary>
    /// Checks if this is a discovery response packet.
    /// </summary>
    public static bool IsResponse(ReadOnlySpan<byte> data)
    {
        return TryParseHeader(data, out var type) && type == RESPONSE;
    }
    
    private static void WriteHandshakeAndType(Span<byte> buffer, byte messageType)
    {
        // Write handshake (big-endian)
        unchecked
        {
            buffer[0] = (byte)(HANDSHAKE >> 56);
            buffer[1] = (byte)(HANDSHAKE >> 48);
            buffer[2] = (byte)(HANDSHAKE >> 40);
            buffer[3] = (byte)(HANDSHAKE >> 32);
            buffer[4] = (byte)(HANDSHAKE >> 24);
            buffer[5] = (byte)(HANDSHAKE >> 16);
            buffer[6] = (byte)(HANDSHAKE >> 8);
            buffer[7] = (byte)HANDSHAKE;
        }
        buffer[8] = messageType;
    }
}

/// <summary>
/// Information about a discovered lobby.
/// </summary>
public sealed class DiscoveredLobbyInfo
{
    public string LobbyId { get; set; } = string.Empty;
    public string LobbyName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool HasPassword { get; set; }
    public LobbyPrivacy PrivacyMode { get; set; }
    public int Port { get; set; }
    public int PublicPort { get; set; }
    public string? PublicAddress { get; set; }
    public string TransportId { get; set; } = "LiteNetLib";
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; }
    public string[] PlayerNames { get; set; } = Array.Empty<string>();
    public int[] PlayerInstruments { get; set; } = Array.Empty<int>();
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Session type: 0 = Server (persistent, bookmarkable), 1 = Lobby (ephemeral UPnP, not bookmarkable).
    /// </summary>
    public int SessionType { get; set; }
    
    // Gameplay settings visible before joining
    /// <summary>
    /// Whether No Fail mode is enabled for this lobby.
    /// </summary>
    public bool NoFailMode { get; set; }
    
    /// <summary>
    /// Whether only shared songs can be played.
    /// </summary>
    public bool SharedSongsOnly { get; set; }
    
    /// <summary>
    /// Maximum band size (0 = unlimited).
    /// </summary>
    public int BandSize { get; set; }
    
    /// <summary>
    /// Allowed game modes for this lobby (as int values). Empty means all modes allowed.
    /// </summary>
    public int[] AllowedGameModes { get; set; } = Array.Empty<int>();
    
    /// <summary>
    /// Whether this lobby is hosted by a dedicated server (no host player).
    /// </summary>
    public bool IsDedicatedServer { get; set; }
}

/// <summary>
/// Lobby privacy modes.
/// </summary>
public enum LobbyPrivacy
{
    Public = 0,
    FriendsOnly = 1,
    Private = 2
}

/// <summary>
/// Builder for discovery response packets.
/// </summary>
public sealed class DiscoveryResponseBuilder
{
    private readonly List<byte> _buffer = new();
    
    public DiscoveryResponseBuilder()
    {
        // Write header
        WriteLong(DiscoveryProtocol.HANDSHAKE);
        _buffer.Add(DiscoveryProtocol.RESPONSE);
    }
    
    public DiscoveryResponseBuilder WithLobbyInfo(DiscoveredLobbyInfo lobby)
    {
        WriteString(lobby.LobbyId);
        WriteString(lobby.LobbyName);
        WriteString(lobby.HostName);
        WriteInt(lobby.CurrentPlayers);
        WriteInt(lobby.MaxPlayers);
        WriteBool(lobby.HasPassword);
        WriteInt((int)lobby.PrivacyMode);
        WriteInt(lobby.Port);
        WriteInt(lobby.PublicPort);
        WriteString(lobby.PublicAddress ?? string.Empty);
        WriteString(lobby.TransportId);
        
        // Player names
        WriteInt(lobby.PlayerNames?.Length ?? 0);
        if (lobby.PlayerNames != null)
        {
            foreach (var name in lobby.PlayerNames)
            {
                WriteString(name ?? string.Empty);
            }
        }
        
        // Player instruments
        WriteInt(lobby.PlayerInstruments?.Length ?? 0);
        if (lobby.PlayerInstruments != null)
        {
            foreach (var instrument in lobby.PlayerInstruments)
            {
                WriteInt(instrument);
            }
        }
        
        // Gameplay settings (added for lobby browser preview)
        WriteBool(lobby.NoFailMode);
        WriteBool(lobby.SharedSongsOnly);
        WriteInt(lobby.BandSize);
        
        // Allowed game modes
        WriteInt(lobby.AllowedGameModes?.Length ?? 0);
        if (lobby.AllowedGameModes != null)
        {
            foreach (var mode in lobby.AllowedGameModes)
            {
                WriteInt(mode);
            }
        }
        
        // Session type (0 = Server, 1 = Lobby)
        WriteInt(lobby.SessionType);
        
        // Dedicated server flag
        WriteBool(lobby.IsDedicatedServer);
        
        return this;
    }
    
    public byte[] Build() => _buffer.ToArray();
    
    private void WriteLong(long value)
    {
        _buffer.Add((byte)(value >> 56));
        _buffer.Add((byte)(value >> 48));
        _buffer.Add((byte)(value >> 40));
        _buffer.Add((byte)(value >> 32));
        _buffer.Add((byte)(value >> 24));
        _buffer.Add((byte)(value >> 16));
        _buffer.Add((byte)(value >> 8));
        _buffer.Add((byte)value);
    }
    
    private void WriteInt(int value)
    {
        _buffer.Add((byte)(value >> 24));
        _buffer.Add((byte)(value >> 16));
        _buffer.Add((byte)(value >> 8));
        _buffer.Add((byte)value);
    }
    
    private void WriteBool(bool value)
    {
        _buffer.Add(value ? (byte)1 : (byte)0);
    }
    
    private void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteInt(bytes.Length);
        _buffer.AddRange(bytes);
    }
}

/// <summary>
/// Parser for discovery response packets.
/// </summary>
public ref struct DiscoveryResponseParser
{
    private ReadOnlySpan<byte> _data;
    private int _position;
    
    public DiscoveryResponseParser(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = DiscoveryProtocol.MIN_PACKET_SIZE; // Skip header
    }
    
    public bool TryParseLobbyInfo(out DiscoveredLobbyInfo lobby)
    {
        lobby = new DiscoveredLobbyInfo();
        
        try
        {
            lobby.LobbyId = ReadString();
            lobby.LobbyName = ReadString();
            lobby.HostName = ReadString();
            lobby.CurrentPlayers = ReadInt();
            lobby.MaxPlayers = ReadInt();
            lobby.HasPassword = ReadBool();
            lobby.PrivacyMode = (LobbyPrivacy)ReadInt();
            lobby.Port = ReadInt();
            lobby.PublicPort = ReadInt();
            lobby.PublicAddress = ReadString();
            lobby.TransportId = ReadString();
            
            // Player names
            int playerNamesCount = ReadInt();
            lobby.PlayerNames = new string[playerNamesCount];
            for (int i = 0; i < playerNamesCount; i++)
            {
                lobby.PlayerNames[i] = ReadString();
            }
            
            // Player instruments
            int playerInstrumentsCount = ReadInt();
            lobby.PlayerInstruments = new int[playerInstrumentsCount];
            for (int i = 0; i < playerInstrumentsCount; i++)
            {
                lobby.PlayerInstruments[i] = ReadInt();
            }
            
            // Gameplay settings (optional for backward compatibility with older hosts)
            if (HasMoreData())
            {
                lobby.NoFailMode = ReadBool();
                lobby.SharedSongsOnly = ReadBool();
                lobby.BandSize = ReadInt();
                
                // Allowed game modes
                int gameModesCount = ReadInt();
                lobby.AllowedGameModes = new int[gameModesCount];
                for (int i = 0; i < gameModesCount; i++)
                {
                    lobby.AllowedGameModes[i] = ReadInt();
                }
                
                // Session type (optional for backward compatibility)
                if (HasMoreData())
                {
                    lobby.SessionType = ReadInt();
                    
                    // Dedicated server flag (optional for backward compatibility)
                    if (HasMoreData())
                    {
                        lobby.IsDedicatedServer = ReadBool();
                    }
                }
            }
            
            lobby.IsActive = true;
            lobby.LastSeen = DateTime.UtcNow;
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private bool HasMoreData()
    {
        return _position < _data.Length;
    }
    
    private int ReadInt()
    {
        if (_position + 4 > _data.Length)
            throw new InvalidOperationException("Not enough data");
            
        int value = (_data[_position] << 24) |
                   (_data[_position + 1] << 16) |
                   (_data[_position + 2] << 8) |
                   _data[_position + 3];
        _position += 4;
        return value;
    }
    
    private bool ReadBool()
    {
        if (_position >= _data.Length)
            throw new InvalidOperationException("Not enough data");
            
        bool value = _data[_position] != 0;
        _position++;
        return value;
    }
    
    private string ReadString()
    {
        int length = ReadInt();
        if (length < 0 || _position + length > _data.Length)
            throw new InvalidOperationException("Invalid string length");
            
        var value = Encoding.UTF8.GetString(_data.Slice(_position, length));
        _position += length;
        return value;
    }
}

/// <summary>
/// Manages discovered lobbies with timeout tracking.
/// Thread-safe.
/// </summary>
public sealed class DiscoveryManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DiscoveredLobbyInfo> _lobbies = new();
    
    /// <summary>
    /// Raised when a new lobby is discovered.
    /// </summary>
    public event Action<DiscoveredLobbyInfo>? LobbyDiscovered;
    
    /// <summary>
    /// Raised when an existing lobby is updated (refreshed).
    /// </summary>
    public event Action<DiscoveredLobbyInfo>? LobbyUpdated;
    
    /// <summary>
    /// Raised when a lobby times out.
    /// </summary>
    public event Action<string>? LobbyLost;
    
    /// <summary>
    /// Gets all currently discovered lobbies.
    /// </summary>
    public IReadOnlyDictionary<string, DiscoveredLobbyInfo> Lobbies
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, DiscoveredLobbyInfo>(_lobbies);
            }
        }
    }
    
    /// <summary>
    /// Adds or updates a discovered lobby.
    /// </summary>
    /// <param name="lobby">The lobby info.</param>
    /// <returns>True if this is a new lobby, false if it was updated.</returns>
    public bool AddOrUpdate(DiscoveredLobbyInfo lobby)
    {
        if (lobby == null || string.IsNullOrEmpty(lobby.LobbyId))
            return false;
            
        bool isNew;
        lock (_lock)
        {
            isNew = !_lobbies.ContainsKey(lobby.LobbyId);
            lobby.LastSeen = DateTime.UtcNow;
            _lobbies[lobby.LobbyId] = lobby;
        }
        
        if (isNew)
        {
            LobbyDiscovered?.Invoke(lobby);
        }
        else
        {
            LobbyUpdated?.Invoke(lobby);
        }
        
        return isNew;
    }
    
    /// <summary>
    /// Removes lobbies that haven't been seen within the timeout.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    public void CleanupOldLobbies(TimeSpan timeout)
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<string>();
        
        lock (_lock)
        {
            foreach (var kvp in _lobbies)
            {
                if (now - kvp.Value.LastSeen > timeout)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var id in toRemove)
            {
                _lobbies.Remove(id);
            }
        }
        
        foreach (var id in toRemove)
        {
            LobbyLost?.Invoke(id);
        }
    }
    
    /// <summary>
    /// Clears all discovered lobbies.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lobbies.Clear();
        }
    }
    
    /// <summary>
    /// Gets a lobby by ID.
    /// </summary>
    public DiscoveredLobbyInfo? GetLobby(string lobbyId)
    {
        lock (_lock)
        {
            return _lobbies.TryGetValue(lobbyId, out var lobby) ? lobby : null;
        }
    }
}
