# Development guide

## Requirements

- .NET SDK `10.0.401` or a newer `10.0.x` patch accepted by `global.json`
- Node.js and npm, only for Playwright UI tests
- Chromium installed through Playwright
- A current desktop browser for manual testing

No database, Redis server, image pipeline, or separate frontend server is
required.

## First run

```powershell
dotnet restore ProjectMahjong.slnx
dotnet run --project src/ProjectMahjong.Server
```

Open `https://localhost:7069`. The ASP.NET Core server hosts the Blazor
WebAssembly files, REST API, and SignalR hub. Do not start the client project
separately.

The local certificate may need to be trusted once:

```powershell
dotnet dev-certs https --trust
```

One browser profile represents one guest identity. Use separate browser
profiles, private windows, or Playwright contexts when testing multiple humans.
Starting with fewer than four humans fills the empty seats with bots.

## Solution map

| Path | Responsibility | Depends on |
|---|---|---|
| `src/ProjectMahjong.Contracts` | Shared REST, hub, and snapshot DTOs | Nothing |
| `src/ProjectMahjong.Game` | Tiles, state machine, legal actions, wins, scoring | Contracts |
| `src/ProjectMahjong.Server` | Rooms, identity, concurrency, bots, SignalR, hosting | Contracts, Game, Client |
| `src/ProjectMahjong.Client` | Chinese Blazor WebAssembly UI | Contracts |
| `tests/ProjectMahjong.Game.Tests` | Deterministic engine tests | Game |
| `tests/ProjectMahjong.Server.Tests` | Room behavior and privacy tests | Server |
| `tests/e2e` | Four-browser responsive gameplay | Running Server |

The dependency direction is intentional. The game engine must not reference
ASP.NET Core, SignalR, browser APIs, or UI components.

## Where to start reading

1. `GameContracts.cs` for the data visible to a browser.
2. `FuzhouGame.cs` and `GameState.cs` for game transitions.
3. `RoomSession.cs` for one synchronized online room.
4. `RoomRegistry.cs` and `GameHub.cs` for routing and broadcasts.
5. `GameConnection.cs` and `Room.razor` for client orchestration.
6. `Components/PlayerPanel.razor` and `MahjongTile.razor` for table rendering.

## Client structure

Pages own navigation and orchestration; components own small presentation
sections:

| File | Purpose |
|---|---|
| `Pages/Home.razor` | Create, join, invite, and resume entry points |
| `Pages/Room.razor` | Session loading, connection state, selected tile, commands |
| `Pages/Rules.razor` | Player-facing rule summary |
| `Components/RoomLobby.razor` | Seats, ready state, and start controls |
| `Components/GameStatusBar.razor` | Hand number, dealer, Gold, wall count |
| `Components/PlayerPanel.razor` | Hand, drawn tile, flowers, melds, discards |
| `Components/LastDiscardPanel.razor` | Shared latest discard and claim countdown |
| `Components/GameActionBar.razor` | Actions already authorized by the snapshot |
| `Components/GameResultDialog.razor` | Centered result and next-hand control |
| `Components/MahjongTile.razor` | Reusable inline-SVG tile face and click events |
| `Components/GameUi.cs` | Pure display helpers |

Razor CSS isolation keeps each component's layout next to the component. Global
tokens, buttons, inputs, and accessibility defaults are in
`wwwroot/css/app.css`. Use `::deep` only when a parent component intentionally
sizes a child component.

The table is rotated conceptually around the viewer by `Room.SeatPosition`:
the viewer is always bottom, then the following seats are right, top, and left.
The drawn tile remains outside the sorted hand at the far right until discard.

## Local identity and reconnect

`SessionStorage` saves one `PlayerSessionDto` per room in browser
`localStorage`. It contains the room code, player ID, display name, and a secret
reconnect token. `GameConnection` authenticates with `Resume` after the initial
SignalR connection and after every automatic reconnect.

Automatic retry delays are 0, 2, 5, and 10 seconds. ASP.NET Core stateful
reconnect is also enabled. If all retries fail, the UI asks for a refresh; the
same stored token can reclaim the seat while the room still exists.

Treat the reconnect token as a credential:

- never log it;
- never put it in a URL;
- require HTTPS outside local development;
- do not use it as permanent account identity.

## Configuration

The defaults live under `FuzhouRules` in
`src/ProjectMahjong.Server/appsettings.json`:

| Key | Default | Meaning |
|---|---:|---|
| `StartingScore` | 100 | Score assigned at match start/rematch |
| `TotalHands` | 4 | Hands before `MatchEnded` |
| `BaseHandPoints` | 3 | Base value used by scoring |
| `ActionTimeoutSeconds` | 60 | Time for every discard or claim decision |
| `EmptyRoomLifetimeMinutes` | 30 | Disconnected room retention |

Production values can be overridden without editing the file, for example:

```text
FuzhouRules__ActionTimeoutSeconds=60
FuzhouRules__EmptyRoomLifetimeMinutes=60
```

These settings are captured when `RoomRegistry` starts. Restart the process to
apply changes; existing rooms cannot survive that restart. `Rules.razor` and
`RULES.md` currently describe the defaults as static text, so update both when a
player-visible production value is changed permanently.

## Current limitations

- No database, saved match, ranking, replay, or audit history.
- No user accounts, password recovery, spectators, chat, or moderation.
- No player replacement, voluntary seat leave, or host migration after room
  creation.
- New players cannot join after the host starts; bot seats are fixed for that
  match.
- Bots use a transparent rule-based strategy and do not estimate hand quality.
- One process is required; there is no cross-instance room coordination.
- Claim timers and reconnect state disappear on restart.
- Special variants listed as unsupported in `RULES.md` have no dedicated score
  handling.

These are intentional scope boundaries. Adding one usually affects more than the
UI; review `ARCHITECTURE.md` before expanding them.

## Common change recipes

### Change a rule or score

1. Write or update a deterministic test in `ProjectMahjong.Game.Tests`.
2. Change `FuzhouRuleOptions`, `WinValidator`, or `FuzhouGame`.
3. Expose any new result through `GameContracts.cs` and `RoomSession`.
4. Update `RULES.md` and `Pages/Rules.razor` together.
5. Run all .NET tests and the browser test if visible behavior changed.

### Add a player action

Change the full chain in this order:

```text
FuzhouGame
  -> RoomSession
  -> RoomRegistry
  -> GameHub
  -> GameConnection
  -> Room.razor
  -> presentation component
```

Legal availability belongs in `AvailableActionsDto`; the UI should not infer
legality from tiles. Game-changing commands need a new command ID and expected
revision, as described in `PROTOCOL.md`.

### Change a DTO

Contracts are shared by the client and server, so update both sides in one
commit. Add fields when possible. Renaming/removing a serialized field can break
an already-loaded PWA client until it refreshes.

### Change the table layout

Keep the four directional classes and component boundaries stable when
possible. Verify at least:

- mobile width at or below 700 CSS pixels;
- 1920×1080 at Windows 125% and 150%;
- an ultrawide display at 125% and 150%;
- a full 17-tile local hand plus a separated drawn tile;
- long discard rivers, melds, flowers, Gold badge, and result dialog.

The Playwright suite already covers these desktop DPI profiles.

### Change tile artwork

Edit the suit-specific SVG in `MahjongTile.razor` and its isolated CSS. Preserve
the `0 0 60 84` view box, accessible button label, `Compact` rendering, Gold
highlight, selection state, and disabled behavior. Artwork is code-owned and has
no external asset or font dependency.

## Code conventions

- Nullable reference types, implicit usings, and recommended analyzers are on.
- Warnings are errors; a clean build must report zero warnings.
- Prefer records for wire values and small result values.
- Prefer explicit domain names over generic managers or helpers.
- Keep methods short around state transitions, but do not split rules across
  layers.
- Use async APIs through the room/server boundary.
- Keep Chinese text at the user-facing boundary; identifiers and comments stay
  English.
- Do not optimize snapshots or collections without a measured need. The target
  is fewer than 100 concurrent players and readability has priority.

## Before submitting a change

```powershell
dotnet build ProjectMahjong.slnx -c Release --no-restore
dotnet test ProjectMahjong.slnx -c Release --no-build
npm run test:e2e
```

Also manually check the affected Chinese text and update the appropriate file in
`docs/` when behavior, configuration, protocol, or operations changed.
