# Architecture

## System shape

Project Mahjong is one ASP.NET Core process with an in-memory room registry. It
serves the Blazor WebAssembly client, room-entry REST endpoints, and the SignalR
game hub from the same origin.

```text
Blazor browser
  |-- REST: create / join / recover seat
  `-- SignalR: resume / commands / private snapshots
                          |
                          v
                     GameHub
                          |
                          v
RoomRegistry --> RoomSession --> FuzhouGame --> GameState
     |                |
     |                `--> viewer-specific RoomSnapshotDto
     `--> connection index, broadcast, timeout scheduling
```

There is no database, cache, message broker, account service, or separate game
worker. This is deliberate for a small private deployment and keeps one obvious
source of truth.

## Ownership by layer

### Contracts

`ProjectMahjong.Contracts` contains values that cross the browser/server
boundary. It has no framework dependency. The important boundary is
`RoomSnapshotDto`: it is already filtered for one viewer and is safe to render.

### Game engine

`ProjectMahjong.Game` owns tiles, hands, the wall, legal actions, claims, wins,
dealer movement, and scores. `FuzhouGame` mutates one `GameState` synchronously
and returns `EngineActionResult` rather than throwing for expected illegal
actions.

The engine knows seats and display names but knows nothing about rooms,
connections, HTTP, timeouts, or components. Randomness is behind
`IRandomSource`, allowing deterministic tests.

### Room server

`RoomSession` owns one room's participants, reconnect secrets, engine instance,
revision, claim deadline, and command de-duplication. A `SemaphoreSlim` protects
all reads and writes, so one room is mutated serially even when several SignalR
calls arrive together.

`RoomRegistry` owns:

- the case-insensitive room-code dictionary;
- connection ID to room/player lookup;
- dispatch from hub calls to a room;
- bot advancement after accepted commands;
- per-viewer snapshot broadcasts;
- claim timeout tasks and expired-room cleanup.

`GameHub` is intentionally thin. It converts SignalR method calls into registry
calls and reports disconnects.

### Client

`RoomApiClient` handles room-entry HTTP. `SessionStorage` persists guest recovery
data. `GameConnection` owns one SignalR connection and the latest accepted
snapshot. `Room.razor` maps connection/snapshot state to small presentation
components.

Components never calculate legal moves. They render `AvailableActionsDto` and
raise user intentions. The server accepts or rejects every intention again.

## Game and room state

The engine phases are:

```text
NotStarted
    |
    v
AwaitingDiscard -- discard has candidates --> AwaitingClaims
    ^                                          | claim resolved
    | unclaimed discard draws next tile        |
    +------------------------------------------+
    |
    +-- win or empty wall --> Ended
```

The room projects these phases into UI stages:

```text
no engine                 -> Lobby
AwaitingDiscard           -> Playing
AwaitingClaims            -> WaitingForClaims
Ended, hands remaining    -> HandEnded
Ended, final hand reached -> MatchEnded
```

Do not add independent phase flags to the browser. Extend the engine state and
derive transport state in `RoomSession`.

## Command flow

```text
click/tap
  -> Room.razor
  -> GameConnection(method, commandId, expectedRevision)
  -> GameHub
  -> RoomRegistry
  -> RoomSession semaphore
  -> FuzhouGame validation and mutation
  -> bots advance until a human decision
  -> private snapshot built for each connected player
  -> SnapshotUpdated broadcast
  -> browser replaces its local snapshot
```

Full snapshots are intentionally used instead of deltas. State is small, the
player count is bounded at four per room, and replacement makes reconnect and
debugging much easier than client-side event replay.

## Privacy and trust boundary

The browser is untrusted:

- it never receives opponents' concealed tiles;
- it cannot choose another seat after `Resume` binds the connection;
- legal actions and score changes are calculated on the server;
- tile IDs are validated against server-owned hands;
- reconnect tokens are compared in constant time;
- stale ordinary commands are rejected by revision;
- retried commands are de-duplicated by command ID.

This is appropriate for a private game, not a complete hostile-internet security
model. There is no account authentication, rate limiting, moderation, audit log,
or encrypted persistence. HTTPS and a private reconnect token remain required
in production.

## Dealing and tile ownership

`TileSet.Create` creates 144 unique tile instances. In this implemented Fuzhou
variant, only Characters, Dots, and Bamboo remain concealed; Winds, Dragons, and
the eight Flowers are all bonus tiles and are exposed/replaced from the back of
the wall.

After initial replacements, one suited tile is removed from the back as the
Gold indicator. Tiles of the same `TileType` act as Gold wildcards in concealed
win validation. A drawn tile's ID is tracked separately so the client can show
it at the far right; it remains part of the server hand and is not a separate
ownership location.

## Claims and timers

After a discard, the engine computes eligible claim possibilities. The room
publishes a UTC deadline using configured `ActionTimeoutSeconds` (60 by default)
for both turn discards and claim decisions. The browser only displays the
countdown; the server owns timeout resolution.

Several players can answer the same snapshot. Claim commands therefore allow a
stale expected revision, while eligibility and duplicate answers are still
validated under the room lock. Resolution waits until every eligible player has
answered or the server timeout supplies Pass for missing answers.

Priority is `Hu > Peng/Kong > Chi`; equal priority goes to the closest seat
after the discarder.

## Computer players

When the host starts with fewer than four humans, `RoomSession` permanently
fills empty seats for that match with bots. Bots use the same engine methods as
humans and advance on the server.

The deliberately simple strategy is:

1. self-draw win when legal;
2. concealed Kong, then added Kong;
3. for a discard: Hu, Kong, Peng, first Chi, otherwise Pass;
4. otherwise discard the last non-Gold tile, falling back to the final tile.

`RunBotsAsync` is capped at 200 steps as a safety guard and stops whenever a
human decision or unresolved human claim is required.

## Identity, disconnect, and room lifetime

The room creator is host and East. Joining players receive the next free seat.
Each human has a random player ID and 128-bit reconnect token. The browser saves
these in `localStorage`.

A disconnect marks the participant offline but does not remove the seat. After
a human has connected once, the server temporarily runs that seat through the
bot policy while it remains disconnected. A valid resume replaces the old
connection ID and receives a new private snapshot. Rooms are checked once per
minute and removed when both conditions
hold:

- no human is connected;
- `LastActivityUtc` is older than `EmptyRoomLifetimeMinutes` (30 by default).

Restarting the process removes every room immediately. Deploy between matches.

## UI composition and artwork

`Room.razor` owns connection/command state and composes `RoomLobby`,
`GameStatusBar`, `PlayerPanel`, `LastDiscardPanel`, `GameActionBar`, and
`GameResultDialog`. `MahjongTile` is shared everywhere and draws all faces with
inline SVG/CSS. There are no external artwork or font licenses.

The table uses CSS Grid for top/right/bottom/left placement. The bottom panel is
the local player and owns the large hand. Component-scoped media queries cover
mobile, short desktop, standard desktop, and ultrawide layouts.

## Extension boundaries

| Change | Primary owner |
|---|---|
| Tile inventory/code/label | `Tiles.cs` |
| Winning shape | `WinValidator.cs` |
| Turn, claim, or score rule | `FuzhouGame.cs` |
| Tunable rule value | `FuzhouRuleOptions.cs` |
| Viewer-visible data/action | Contracts + `RoomSession.BuildSnapshot` |
| Room identity/lifecycle | `RoomSession` / `RoomRegistry` |
| Wire method | Hub + registry + connection |
| Page orchestration | `Room.razor` |
| Visual presentation | Component `.razor` + isolated `.razor.css` |

If persistence is introduced later, persist an explicit room/game document
behind `RoomSession`. Do not make the client authoritative. If multiple app
instances are introduced, room affinity and cross-instance state/locking must be
designed first; simply adding replicas is unsafe.
