# Developer documentation

This directory is the maintenance guide for Project Mahjong. The source code and
developer documentation are English; all player-facing text remains Simplified
Chinese.

## Reading order

1. [Development](DEVELOPMENT.md) — set up the repository, run it, and find the
   right code for a change.
2. [Architecture](ARCHITECTURE.md) — understand ownership, state flow,
   concurrency, reconnects, and room lifetime.
3. [Realtime protocol](PROTOCOL.md) — REST endpoints, SignalR methods, snapshots,
   revisions, and command IDs.
4. [Rules](RULES.md) — the exact house rules implemented by the server.
5. [Testing](TESTING.md) — unit, room, and browser test workflows.
6. [Deployment](DEPLOYMENT.md) — operate one production instance on a VPS.

## Non-negotiable design constraints

- The server is authoritative. A browser sends intentions and never calculates
  legal moves, wins, or scores.
- Rooms are intentionally in memory. A process restart ends every active match.
- Run exactly one application instance. There is no shared room store.
- Only the viewing player receives their concealed hand.
- Prefer complete, readable snapshots over client-side state reconciliation.
- Keep game rules in `ProjectMahjong.Game`, transport in
  `ProjectMahjong.Server`, shared wire types in `ProjectMahjong.Contracts`, and
  rendering in `ProjectMahjong.Client`.
- Player-facing UI and errors must be Simplified Chinese.

## Quick verification

```powershell
dotnet restore ProjectMahjong.slnx
dotnet build ProjectMahjong.slnx --no-restore
dotnet test ProjectMahjong.slnx --no-build
npm install
npx playwright install chromium
npm run test:e2e
```

Run the browser suite after building Release because its web server starts with
`--no-build`.
