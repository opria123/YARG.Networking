# YARG.Networking

Networking logic and server runtimes for YARG online multiplayer. This repo hosts the future transport/runtime stack that both the Unity client and the dedicated server consume.

## Solution Layout

| Project | Target | Purpose |
| --- | --- | --- |
| `YARG.Net` | `netstandard2.1` | Shared abstractions plus the concrete LiteNetLib transport that Unity and server hosts reference. |
| `YARG.ServerHost` | `net8.0` | Console application that boots the LiteNetLib transport via the default server runtime. |
| `YARG.Introducer` | `net8.0` | Future introducer / relay control plane for NAT traversal. |
| `YARG.Net.Tests` | `net8.0` | xUnit test suite that exercises the shared library with transport shims. |

The root `YARG.Networking.sln` loads all of the projects above. Shared SDK settings live in `Directory.Build.props`.

## Building

```bash
dotnet restore YARG.Networking.sln
dotnet build YARG.Networking.sln --configuration Release
dotnet test YARG.Networking.sln --configuration Release
```

The GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs the same restore/build/test sequence on every PR and push.

## Transports

`YARG.Net` now ships two transports:

- `NullTransport` keeps unit tests fast by providing a no-op loopback implementation.
- `LiteNetLibTransport` wraps the upstream LiteNetLib stack and is the default choice for gameplay and server runtimes. It already supports server/client start, connection events, and payload forwarding; higher level runtimes will eventually own the polling cadence.

## Runtime Abstractions

- `DefaultServerRuntime` (in `YARG.Net.Runtime`) drives a configured `INetTransport` with a simple polling loop, exposing `Configure`, `StartAsync`, and `StopAsync` for dedicated hosts.
- `DefaultClientRuntime` (also under `YARG.Net.Runtime`) manages client connections by registering a transport, issuing `ConnectAsync` / `DisconnectAsync`, and internally running the polling loop needed by LiteNetLib on the Unity player.
- `YARG.ServerHost` wires the server runtime to `LiteNetLibTransport`, hosts the shared `PacketDispatcher`, and now bootstraps the `ServerHandshakeHandler`/`LobbyStateManager` pipeline. The CLI supports `--port <value>` (default `7777`), `--nat`, `--max-players <value>`, and `--password <value>` to gate access. Press `Ctrl+C` to request shutdown at runtime.
- Both runtimes can now forward transport payloads into a shared `IPacketDispatcher`, allowing Unity and the dedicated host to share the same packet-handling logic from `YARG.Net`.

## Serialization

- `INetSerializer` abstracts payload encoding. `JsonNetSerializer` provides the default implementation backed by `System.Text.Json`.
- `NetSerializerOptions.CreateDefault()` centralizes the JSON configuration (camelCase, enum-as-string, ignore nulls) so Unity clients and dedicated servers produce identical payloads.
- Higher-level packets can depend on `INetSerializer` to remain agnostic of the underlying transport wire format.

## Packet DTOs

- `PacketType` lists the current protocol messages (`HandshakeRequest`, `HandshakeResponse`, `Heartbeat`, `LobbyState`, `LobbyInvite`, `SongSelection`, `GameplayCountdown`, `GameplayInputFrame`).
- Each payload implements the `IPacketPayload` marker record (see the files under `src/YARG.Net/Packets/`).
- `PacketEnvelope<TPayload>` wraps the payload with `Type` metadata and the `ProtocolVersion.Current` string so routers can inspect and dispatch without deserializing blindly.
- As we add more gameplay/session packets, update `PacketType` and create new payload records that serialize through the shared `INetSerializer`.

### Packet Dispatching

- `PacketDispatcher` + `IPacketDispatcher` live in `YARG.Net.Packets.Dispatch` and provide a registry for mapping `PacketType` values to strongly-typed handlers.
- Handlers receive a `PacketContext` (connection, channel, endpoint role) and the fully deserialized `PacketEnvelope<T>`.
- Hosts simply plug the dispatcher into the runtimes; the shared library owns packet parsing, keeping Unity and the server thin.

## Handlers & Lobby Flow

- `ServerHandshakeHandler` validates protocol versions, player names, and optional passwords before minting `SessionRecord` entries inside `SessionManager`.
- `LobbyStateManager` and `ServerLobbyCoordinator` subscribe to those session events to keep lobby membership, roles, song selection, and readiness synchronized across every peer.
- When any lobby event fires, the coordinator emits a fresh `LobbyStatePacket` via the shared dispatcher so Unity and dedicated-host clients receive identical snapshots.
- On the client, `ClientLobbyStateHandler` listens for `LobbyStatePacket` dispatches, caches the latest snapshot, and raises `LobbyStateChanged` events that UI layers can bind to without touching transport state.
- `ClientLobbyCommandSender` serializes ready-state toggles and song selection commands, while `ServerLobbyCommandHandler` validates them against `SessionManager`/`LobbyStateManager` before mutating lobby state.

### Server Host CLI Cheatsheet

```bash
dotnet run --project src/YARG.ServerHost -- \
	--port 7779 \
	--nat \
	--max-players 12 \
	--password supersecret
```

Use only the flags you need; unspecified values fall back to the defaults listed above.

### Client Runtime Wiring Example

```csharp
var serializer = new JsonNetSerializer();
var dispatcher = new PacketDispatcher(serializer);
var lobbyHandler = new ClientLobbyStateHandler();
lobbyHandler.Register(dispatcher);

var clientRuntime = new DefaultClientRuntime();
clientRuntime.RegisterTransport(liteNetLibTransport);
clientRuntime.RegisterPacketDispatcher(dispatcher);

lobbyHandler.LobbyStateChanged += (_, args) =>
{
	Debug.Log($"Lobby status: {args.Snapshot.Status}");
};

var commandSender = new ClientLobbyCommandSender(serializer);
Guid sessionId = Guid.Empty;

runtime.Connected += (_, args) =>
{
	// Cache the remote connection when the handshake finishes so we can send commands later.
	sessionId = /* populate from HandshakeResponsePacket */;
};

runtime.Disconnected += (_, _) => sessionId = Guid.Empty;

void ToggleReady(bool ready)
{
	var connection = runtime.ActiveConnection;
	if (connection is null || sessionId == Guid.Empty)
	{
		return;
	}

	commandSender.SendReadyState(connection, sessionId, ready);
}
```

Once registered, any lobby broadcast from the server automatically updates the handler’s snapshot and fires the event.

## Next Steps

- Build gameplay/session orchestration layers on top of the new client/server runtime pair.
- Implement serialization helpers so packets can be defined once and shared across platforms.
- Publish `YARG.Net` as a NuGet package so the Unity repo can reference it via `Packages/manifest.json` or assembly definition references.
