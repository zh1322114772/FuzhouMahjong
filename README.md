# Project Mahjong

A readable, self-hosted, four-player Fuzhou Mahjong web app.

The player UI is Simplified Chinese. Source code and developer documentation use English. Active rooms and scores are held only in memory: restarting the server ends all matches.

## Included

- Four-player rooms with short invite codes and share links
- One to four human players; readable dummy AI automatically fills empty seats
- Guest identity with a private reconnect token stored in the browser
- ASP.NET Core SignalR live play and automatic reconnect
- Mobile, tablet, and desktop layouts
- Select-and-confirm or double-click-to-discard controls
- 16-tile Fuzhou hand flow, flowers, Gold, Chi, Peng, Kong, Hu, and scoring
- Player-specific snapshots that never expose opponents' concealed tiles
- Claim priority and a visible 20-second auto-pass timer
- Four-hand match scoreboard and rematch
- Installable Blazor WebAssembly PWA shell
- Unit and room-level tests
- Docker/Caddy single-VPS deployment

## Run locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore ProjectMahjong.slnx
dotnet run --project src/ProjectMahjong.Server
```

Open `https://localhost:7069`. The server hosts the Blazor client and the SignalR hub, so do not run the client project separately.

You can start alone or with two/three people; empty seats become computer players. To test four humans, use four browser profiles/private windows. Browser storage represents one seat, so four ordinary tabs in one profile share an identity.

## Verify changes

```powershell
dotnet build ProjectMahjong.slnx --no-restore
dotnet test ProjectMahjong.slnx --no-restore
```

The Playwright suite starts the Release server, checks a shared four-player
desktop table, and runs a five-hand mobile simulation with iPhone/iPad
landscape emulation and unclipped-tile assertions:

```powershell
npm install
npx playwright install chromium
npm run test:e2e
```

## Project map

| Project | Responsibility |
|---|---|
| `ProjectMahjong.Game` | Tiles, rules, turn state, claim resolution, scoring |
| `ProjectMahjong.Contracts` | DTOs shared by browser and server |
| `ProjectMahjong.Server` | Rooms, guest sessions, SignalR, snapshots, cleanup |
| `ProjectMahjong.Client` | Chinese Blazor WebAssembly UI |
| `*.Tests` | Rules and room behavior |

Start reading at:

1. `FuzhouGame.cs` for gameplay.
2. `RoomSession.cs` for one online room.
3. `RoomRegistry.cs` for room lookup and broadcasting.
4. `Room.razor` for page state and command orchestration.
5. `Components/PlayerPanel.razor` for hands, the separated drawn tile, melds, and discards.
6. `Components/MahjongTile.razor` for the dependency-free SVG tile artwork.

## Configuration

Edit `src/ProjectMahjong.Server/appsettings.json`:

```json
"FuzhouRules": {
  "StartingScore": 100,
  "TotalHands": 4,
  "BaseHandPoints": 3,
  "ActionTimeoutSeconds": 60,
  "EmptyRoomLifetimeMinutes": 30
}
```

## Developer documentation

Start with the [developer documentation index](docs/README.md):

- [development and maintenance](docs/DEVELOPMENT.md)
- [architecture and state ownership](docs/ARCHITECTURE.md)
- [REST and realtime protocol](docs/PROTOCOL.md)
- [implemented rules](docs/RULES.md)
- [testing](docs/TESTING.md)
- [single-VPS deployment and operations](docs/DEPLOYMENT.md)

