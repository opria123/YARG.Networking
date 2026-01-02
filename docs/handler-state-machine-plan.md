# Handler / State Machine Plan

_Last updated: 2025-02-28_

## Objectives
- Drive all handshake and lobby packet handling through `PacketDispatcher` so both Unity and server hosts reuse the exact same logic.
- Provide explicit client/server state machines that surface deterministic events to upper layers (`IClientRuntime`, `IServerRuntime`, future lobby UI).
- Keep transport-specific code isolated (only dispatchers touch `PacketContext`); everything else operates on logical messages and timeouts.

## Architectural Layers
1. **Packet Handlers** (`YARG.Net.Handlers`)
   - Lightweight classes that implement `IPacketHandler` (new abstraction) and register with `IPacketDispatcher` during runtime boot.
   - Responsible for translating raw packets into state machine events and emitting responses/commands.
2. **Session Manager** (`YARG.Net.Sessions`)
   - Tracks connected peers, handshake status, and lobby membership (`SessionId`, `PlayerId`, roles, readiness).
   - Exposes operations like `TryAttachConnection`, `CompleteHandshake`, `UpdatePresence`, `DisconnectSession`.
3. **Lobby State Machine** (`YARG.Net.Sessions.Lobby`)
   - Maintains authoritative lobby state (players, selected song, readiness flags, countdown timer window).
   - Publishes snapshots via `LobbyStatePacket` and raises domain events (player join/leave, ready toggles, song selection changes).

## Handler Contracts
- `IPacketHandlerContext`
  - Surfaces `PacketContext`, the dispatcher, server/client runtime references, and utilities for replying / disconnecting.
- `IOutboundPacketWriter`
  - Thin helper that serializes packets into envelopes and schedules send via `INetTransport`.
- `IHandshakeState`, `ILobbyState`
  - Interfaces to keep handshake logic independent from eventual gameplay state machine.

## Handshake Flow
### Server State Machine
| State | Description | Transitions |
| --- | --- | --- |
| `AwaitingHello` | Connection observed but not verified. | On `HandshakeRequest`: validate version/password → `Validating`. Timeout → `Rejected`. |
| `Validating` | Business logic checks (protocol version compatibility, ban list, lobby capacity). | On success → `Accepted`; on failure → `Rejected`. |
| `Accepted` | Server assigns `SessionId`, registers player with `SessionManager`. | Immediately sends `HandshakeResponsePacket { Accepted = true }` and transitions to `LobbyParticipant`. |
| `Rejected` | Server sends `HandshakeResponsePacket { Accepted = false, Reason }` and schedules disconnect. | Terminal. |
| `LobbyParticipant` | Handshake complete; future packets routed to lobby/gameplay handlers. | Transitions to `Disconnected` on transport close or kick. |

**Validation Rules**
- `HandshakeRequestPacket.ClientVersion` must equal `ProtocolVersion.Current`. Future enhancement: allow compatibility table.
- `PlayerName` sanitized (length ≤ 24, ASCII subset for now). Illegal names cause rejection with reason.
- Optional password/token validation (pulled from runtime options) to unblock private lobbies.

### Client State Machine
| State | Description |
| --- | --- |
| `Disconnected` | No active connection. |
| `SendingHello` | Client writes `HandshakeRequestPacket` immediately after transport connect; starts timeout (default 3 s). |
| `AwaitingResponse` | Waits for `HandshakeResponsePacket`. On `Accepted` → `Ready`. On `Rejected` → go back to `Disconnected` with surfaced reason. |
| `Ready` | Client can start lobby sync. | 

**Timeout Handling**
- Server: drop connections stuck in `AwaitingHello` for >5 s.
- Client: fail handshake if `HandshakeResponsePacket` not received within 3 s (configurable) or transport disconnect occurs.

## Lobby State Machine (Server)
| State | Description | Triggers |
| --- | --- | --- |
| `Idle` | Lobby open, no song selected. | Entry after handshake or after set completes. |
| `SelectingSong` | Host (or delegated player) is choosing a song. | Enter when `SongSelectionPacket` arrives. |
| `ReadyCheck` | Song selected; gather readiness from players. | Enter when host locks selection. |
| `Countdown` | All required players ready; start countdown timer (e.g., 5 s) before gameplay session start event emitted. |

**Events & Packets**
- `PlayerJoined`/`PlayerLeft` (drives `LobbyStatePacket` broadcast).
- `PlayerReadyToggled` → emits `LobbyStatePacket` + host notifications.
- `InviteRequested` → respond with `LobbyInvitePacket` containing `InviteCode`.
- `SongSelected`/`SongCleared` bridging to `SongSelectionPackets`.
- `CountdownStarted`/`CountdownCancelled` to keep UI consistent.

**Data Structures**
- `LobbyPlayerState` (tracks role, readiness, instrument choice, latency metrics).
- `LobbySnapshotBuilder` (immutable struct for broadcast serialization, deduping identical states).
- `LobbySettings` (max players, privacy/password, region tags) provided through runtime options.

## Dispatcher Integration
1. Runtime initialization wires handlers:
   ```csharp
   dispatcher.RegisterHandler(PacketType.HandshakeRequest, handshakeHandler.HandleAsync);
   dispatcher.RegisterHandler(PacketType.LobbyStateRequest, lobbyHandler.HandleStateRequestAsync);
   dispatcher.RegisterHandler(PacketType.LobbyCommand, lobbyHandler.HandleCommandAsync);
   ```
2. Handlers create `PacketContext`-aware responses using the outbound writer; they never touch `INetTransport` directly.
3. `SessionManager` exposes `TryGetConnection(Guid sessionId, out INetConnection connection)` so handlers can broadcast or unicast snapshots.
4. Server runtime ticks lobby state machine on a configurable cadence (e.g., 10 Hz) to evaluate countdown timers and emit broadcasts even without inbound packets.

## Client Synchronization Layer

### Client Lobby State Handler
- Lives under `YARG.Net.Handlers.Client` and registers for `PacketType.LobbyState`.
- Owns a lightweight `ClientLobbyCache` that stores the most recent `LobbyStatePacket`, exposes `LobbySnapshotChanged` events, and provides helper queries (player list, readiness summary, selected song).
- Handler responsibilities:
   1. Validate protocol compatibility and discard out-of-order snapshots (track `LastSequence` or `DateTimeOffset` if we later version packets).
   2. Update the cache and fire change events that Unity/UI layers can observe.
   3. Surface host-only metadata (e.g., whether the local session ID matches the host ID) so menus know when to show start buttons.
- Client runtime integration:
   - Default client runtime receives a `ClientLobbyStateHandler` instance via configuration (similar to how the server runtime receives `PacketDispatcher`).
   - When `ConnectAsync` succeeds and the dispatcher attaches to the transport, the handler begins listening for lobby packets automatically; no Unity-specific wiring needed.
- Testing strategy:
   - Unit tests feed synthetic `LobbyStatePacket` payloads into the handler to ensure caching and event emission work.
   - Integration tests spin up the `DefaultClientRuntime` with a loopback transport and confirm UI observers receive the expected state transitions.

   ## Client Command Pipeline

   ### Goals
   - Allow Unity/UI layers to issue lobby actions (ready toggle, song selection updates) without touching transports or packet envelopes directly.
   - Keep commands typed and validated so invalid payloads are rejected before hitting the wire.
   - Reuse the dispatcher so the same handlers can execute in tests or alternative runtimes.

   ### Proposed Structure
   1. **Packets**
      - Introduce `LobbyCommandPacket` (packet type TBD) with a discriminated union payload:
        - `ReadyToggleCommand(Guid SessionId, bool IsReady)`
        - `SongSelectionCommand(Guid SessionId, string SongId, IReadOnlyList<SongInstrumentAssignment> Assignments)`
      - Commands are always sent client → server; server handlers interpret them and update `LobbyStateManager`.
   2. **Client Command Bus** (`YARG.Net.Client.LobbyCommandSender`)
      - Accepts a transport connection handle (exposed via `DefaultClientRuntime` when connected) plus `INetSerializer`.
      - Provides high-level methods (`SetReadyAsync(bool)`, `SelectSongAsync(...)`) that serialize the proper command packet and push it through the transport on `ReliableOrdered`.
      - Emits `CommandRejected` events if the server responds with an error (future `LobbyCommandAckPacket`).
   3. **Server Command Handler** (`ServerLobbyCommandHandler`)
      - Registers for `PacketType.LobbyCommand` and, per command type, mutates `LobbyStateManager` (set ready, change song) after validating the caller's session/role.
      - On success, relies on `ServerLobbyCoordinator` broadcasts to reflect the change; optionally replies with ack.
   4. **Runtime Integration**
      - `DefaultClientRuntime` exposes a lightweight `ClientConnectionContext` (session ID, `INetConnection`) once handshake succeeds so the command sender can write to the server.
      - `ServerHost` wires the new handler next to the handshake/lobby coordinator so all commands flow through shared logic.

   ### Testing
   - Unit tests: command sender builds proper packets; server handler rejects unauthorized requests (e.g., non-host selecting songs) and updates lobby state when valid.
   - Integration: spin loopback transport, run handshake, send ready toggle, and assert lobby snapshot reflects the change.

## Testing Strategy
- Unit tests around `HandshakeHandler`
  - Accepts valid version/name, rejects mismatched protocol or banned names.
  - Ensures second handshake attempt on same connection is ignored.
- `SessionManagerTests`
  - Concurrent register/unregister, ensures GUID uniqueness, verifies map cleanup on disconnect.
- `LobbyStateMachineTests`
  - Deterministic progression across states, verifying packet emission order and countdown cancellation when a player unreadies.
- Integration-style tests using `LoopbackTransport`
  - Simulate client handshake + lobby join to ensure dispatcher wiring works end-to-end.

## Work Breakdown
1. **Infrastructure**
   - Create `Handlers` namespace, define base interfaces (`IPacketHandlerContext`, `IPacketSender`).
   - Flesh out `SessionManager` with per-connection records and broadcast helpers.
2. **Handshake Handler**
   - Implement server + client states, registration with dispatcher, tests.
3. **Lobby Manager**
   - Build lobby state storage, transitions, and packet serializers.
   - Implement lobby packet handlers (join/leave, ready toggles, invites, song selection, countdown control).
4. **Runtime Integration**
   - Expose handler registration entry points from `DefaultServerRuntime`/`DefaultClientRuntime`.
   - Surface lobby/handshake events through new public APIs for Unity/ServerHost to consume.
5. **Docs & Samples**
   - Update `README.md` with handshake/lobby sections (sequence diagrams, config knobs).
   - Provide ServerHost sample showing handler registration and CLI events.

## Open Questions
- Should session IDs persist across reconnects or reset per transport connection? (Currently planning per-connection new GUID.)
- Where should password/whitelist rules live? (Likely `ServerRuntimeOptions`.)
- Need to decide how invites map to `SessionId` vs. arbitrary codes for future lobby server.
