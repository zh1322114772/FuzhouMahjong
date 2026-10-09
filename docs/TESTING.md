# Testing guide

## Test layers

| Layer | Location | What it protects |
|---|---|---|
| Engine | `tests/ProjectMahjong.Game.Tests` | Tile set, winning shapes, Gold wildcard, deals, turns, claim priority, scoring |
| Room | `tests/ProjectMahjong.Server.Tests` | Four-seat limit, ready/start flow, privacy, reconnect token, bots, shared discards |
| Browser | `tests/e2e/game-table.spec.mjs` | Real SignalR play, four isolated users, interaction, responsive layout, complete hand |

## Fast verification

```powershell
dotnet build ProjectMahjong.slnx -c Release --no-restore
dotnet test ProjectMahjong.slnx -c Release --no-build
```

Warnings are treated as errors. Use Release for the final check because the
browser suite also runs the Release output.

Run one project or one test while iterating:

```powershell
dotnet test tests/ProjectMahjong.Game.Tests --filter FullyQualifiedName~WinValidator
dotnet test tests/ProjectMahjong.Server.Tests --filter FullyQualifiedName~RoomSessionTests
```

## Deterministic engine tests

Use `UnshuffledRandomSource` when exact wall order matters. Tests may arrange
`GameState` directly to isolate a transition, but production commands should
always go through `FuzhouGame` public methods.

For every rule change, test both:

- the accepted/legal path and resulting phase, tiles, score, and turn;
- at least one rejected/illegal path with no unintended state mutation.

Useful invariants include:

- all 144 tile IDs are unique;
- concealed hands contain only suited tiles after flower replacement;
- every tile is in exactly one location;
- only the current player can perform turn actions;
- claimed discards leave the original discard river;
- scores sum to the starting total after payments;
- Gold is excluded from public melds.

## Browser setup

```powershell
npm install
npx playwright install chromium
dotnet build ProjectMahjong.slnx -c Release --no-restore
npm run test:e2e
```

Playwright starts the server at `http://127.0.0.1:5271` with `--no-build`. If
that port is already occupied, stop the old `ProjectMahjong.Server` process or
reuse it only when it was built from the current source.

The test creates four independent browser contexts and uses these effective
Windows scaling profiles:

| Label | Viewport | Device scale |
|---|---:|---:|
| `1080p-125` | 1536×864 | 1.25 |
| `1080p-150` | 1280×720 | 1.5 |
| `ultrawide-125` | 2752×1152 | 1.25 |
| `ultrawide-150` | 2293×960 | 1.5 |

It verifies:

- four directional panels relative to each viewer;
- Gold and the complete local hand are visible without page/hand overflow;
- a drawn tile is separated at the far right and merges after discard;
- double-click discard and select-then-discard both work;
- every player sees the same latest discard;
- claim countdown is checked whenever the shuffle produces a claim;
- the game reaches a centered result dialog;
- no player panel clips tiles and no browser exception occurs.

Screenshots and traces are written under `test-results/`. Keep that directory as
a local artifact, not source documentation.

## Repeated stability run

Use this after changes to rules, concurrency, reconnects, or responsive layout:

```powershell
npx playwright test --repeat-each=5
```

Because shuffling uses `Random.Shared`, repetition explores different hands and
claim paths. A failure should be reproduced with its trace before changing a
timeout or loosening an assertion.

## Manual smoke checklist

- Create a room and join through the copied invitation link.
- Start with one, two, three, and four humans; verify bot fill.
- Refresh a player's page and verify the same seat/hand returns.
- Temporarily disable the network and observe reconnect status.
- Test single-click selection, double-click discard, Gold warning, claims, Kong,
  result, next hand, and rematch when available.
- Check mobile touch scrolling and desktop 125%/150% scaling.
- Confirm opponents never expose concealed tile faces.

## Debugging failures

- **Stale command error:** another snapshot won the race; inspect revision values
  and do not disable optimistic concurrency.
- **E2E cannot bind port 5271:** an old local server is listening.
- **Browser loads old UI:** unregister the service worker or clear site data,
  then reload.
- **Intermittent claim step:** retain and inspect the Playwright trace; claims
  depend on shuffled hands.
- **Layout clipping:** inspect both `scrollWidth/clientWidth` and
  `scrollHeight/clientHeight`; do not hide overflow to make the test pass.
