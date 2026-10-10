# REST and realtime protocol

The app uses same-origin JSON over HTTP for room entry and SignalR for all live
room activity. Shared types are in `ProjectMahjong.Contracts`.

## REST endpoints

| Method and path | Request | Success | Purpose |
|---|---|---|---|
| `GET /api/health` | None | `{ "status": "ok" }` | Process health check |
| `POST /api/rooms` | `CreateRoomRequest` | `SessionResponseDto` | Create a room; creator becomes East and host |
| `POST /api/rooms/{code}/join` | `JoinRoomRequest` | `SessionResponseDto` | Take the next empty seat before play starts |
| `POST /api/rooms/{code}/rejoin` | `RejoinRoomRequest` | `SessionResponseDto` | Validate a saved player ID and reconnect token |

Validation failures return HTTP 400 with `ApiErrorDto`. Display names are
trimmed, required, limited to 8 characters, and unique within a room ignoring
case. Newly created room codes contain exactly six decimal digits.

`SessionResponseDto` contains a private `PlayerSessionDto` plus the first
viewer-specific snapshot. Never send one player's session DTO to another
player.

## SignalR endpoint

The hub path is `/hubs/game`. The browser must call `Resume` after connecting
before issuing room commands.

### Session and lobby methods

| Hub method | Arguments | Notes |
|---|---|---|
| `Resume` | `ResumeRequestDto` | Binds this connection ID to one authenticated seat |
| `SetReady` | `bool ready` | Lobby only |
| `StartGame` | None | Host only; all humans must be ready |
| `StartNextHand` | None | Host only after a hand |
| `StartRematch` | None | Host only after the configured final hand |

### Game methods

Every game method ends with `Guid commandId, long expectedRevision`.

| Hub method | Leading arguments |
|---|---|
| `Discard` | `int tileId` |
| `SelfDrawnWin` | None |
| `ConcealedKong` | `int tileId` |
| `AddedKong` | `int tileId` |
| `Pass` | None |
| `ClaimHu` | None |
| `ClaimPeng` | None |
| `ClaimKong` | None |
| `ClaimChi` | `string optionId` |

All calls return `CommandReplyDto` with `Accepted`, an optional Chinese error,
and the server revision. A successful reply only acknowledges the command; UI
state comes from the next `SnapshotUpdated` event.

### Server-to-client methods

| Client method | Payload | Purpose |
|---|---|---|
| `SnapshotUpdated` | `RoomSnapshotDto` | Replace the viewer's complete room view |
| `Notice` | `string` | Show a transient server error/notice; currently reserved |

## Revision and command safety

Each room has a monotonically increasing `Revision`.

1. A browser creates a new random command ID.
2. It sends the command with the revision from its current snapshot.
3. `RoomSession` serializes the command with its per-room semaphore.
4. For ordinary game commands, a stale revision is rejected.
5. A previously processed command ID is accepted without applying it again.
6. On success, the revision increments and fresh private snapshots are
   broadcast.

Claim commands intentionally accept a stale revision because several eligible
players answer the same discard concurrently. The engine still validates that
the claim window exists, that the player is eligible, and that they have not
already answered.

The processed-command set is bounded pragmatically: it is cleared after it
grows past 1,000 entries. Command IDs only protect short-lived retries; they are
not an audit log.

## Snapshot privacy

`RoomSnapshotDto` is rebuilt separately for every connected participant.

Public fields include stage, turn, dealer, Gold indicator, latest/pending
discard, claim deadline, scores, concealed tile counts, flowers, melds, and
discard rivers. Private fields are:

- `CurrentPlayerId` — identifies the viewer;
- `MyHand` — only the viewer's concealed tiles;
- `MyDrawnTileId` — allows the UI to separate the newest tile;
- `AvailableActions` — legal actions calculated specifically for the viewer.

Never add a server domain object directly to a DTO. Explicitly map the minimum
player-visible data in `RoomSession.BuildSnapshot`.

## Room stages

| `RoomStage` | Meaning | Typical allowed control |
|---|---|---|
| `Lobby` | Players joining and becoming ready | Ready; host start |
| `Playing` | Engine is awaiting a discard | Current player's legal actions |
| `WaitingForClaims` | One discard has claim candidates | Eligible players claim/pass |
| `HandEnded` | Result exists and more hands remain | Host starts next hand |
| `MatchEnded` | Configured hand count reached | Host starts rematch |

`RoomStage` is a transport-level projection of `HandPhase`; do not use it as a
second game state machine.

## Reconnect sequence

```text
Browser                 GameHub             RoomRegistry       RoomSession
   | connect               |                     |                 |
   | Resume(session) ----->| ConnectAsync ------>| authenticate -->|
   |                       |                     | bind connection |
   |<---------------------- SnapshotUpdated for this viewer -------|
   |                       |                     |                 |
   | network interruption  | OnDisconnected --->| mark offline --->|
   | automatic reconnect   |                     |                 |
   | Resume(session) ----->|-------------------->| replace binding  |
   |<---------------------- fresh snapshot ------------------------|
```

If the server restarted or the room expired, resume fails and the player must
create/join a new room. A newer connection replaces the participant's previous
connection ID; a late disconnect from the old connection cannot mark the newer
one offline.

## Claim timer

The deadline is server-owned. `RoomSession` exposes its UTC timestamp and the
client only renders the remaining seconds. On timeout, all missing responses
become Pass, priority is resolved, bots run, and new snapshots are broadcast.

Default claim priority is:

```text
Hu > Peng/Kong > Chi
```

Equal priority goes to the closest seat after the discarder.
