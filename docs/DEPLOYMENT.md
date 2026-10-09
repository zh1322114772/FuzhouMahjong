# Single-VPS deployment

## Supported production shape

Run one application container behind one Caddy container. Caddy terminates TLS,
compresses responses, and proxies HTTP/WebSocket traffic to ASP.NET Core.

Do not scale the `app` service above one replica. Rooms, connection mappings,
claim timers, and scores exist only in that process.

## Prerequisites

- Linux VPS with Docker Engine and Docker Compose v2
- Domain with A/AAAA records pointing to the VPS
- Inbound TCP 80 and TCP/UDP 443 allowed
- Enough memory for the OS, Docker, Caddy, and one .NET process

No application data volume or database is required. Caddy's volumes must be
kept because they contain certificate state.

## Initial deployment

Create `.env` beside `compose.yaml`:

```text
DOMAIN=mahjong.example.com
```

Optional rule overrides can be added under the app's `environment` section in
`compose.yaml`, for example:

```yaml
environment:
  ASPNETCORE_ENVIRONMENT: Production
  FuzhouRules__ClaimTimeoutSeconds: 30
  FuzhouRules__EmptyRoomLifetimeMinutes: 60
```

Build and start:

```bash
docker compose up -d --build
docker compose ps
docker compose logs --tail=100 app
docker compose logs --tail=100 caddy
```

Verify both paths:

```bash
curl -fsS https://mahjong.example.com/api/health
curl -I https://mahjong.example.com/
```

The health response should be `{"status":"ok"}`. Also create a test room in a
browser and verify the connection banner becomes connected; a plain HTTP health
check does not exercise SignalR/WebSocket traffic.

## Visual Studio folder publish

Publish `ProjectMahjong.Server`, not `ProjectMahjong.Client`. The server publish
contains the executable, API, SignalR hub, and client files under `wwwroot`.

Stop any copy running from the destination before publishing because Windows
locks the server DLL. If upgrading from a build that used fingerprinted Blazor
boot files, run **Build > Clean Solution** once before publishing so obsolete
framework files are removed.

After publishing, these two paths must agree:

```text
wwwroot/index.html -> _framework/blazor.webassembly.js
wwwroot/_framework/blazor.webassembly.js
```

The literal marker `#[.{fingerprint}]` must not appear in the published
`index.html`. If a browser was open on the previous broken PWA, close all tabs
for the site and reopen it, or unregister its Service Worker and clear site
storage once.

## Release/update procedure

Every restart destroys active rooms, so coordinate a maintenance window between
matches.

```bash
git pull --ff-only
docker compose build --pull app
docker compose up -d
docker compose ps
docker compose logs --tail=100 app
```

Then run the health and browser smoke checks. Published Blazor apps use a service
worker; if a device appears to keep an old UI, close all app tabs and reopen it,
or clear that site's cached data.

## Rollback

Application state cannot be recovered after an update/restart, but code can be
rolled back:

1. check out the previously deployed commit or image tag;
2. rebuild/start the single app container;
3. run the health and browser smoke checks.

Do not preserve or restore a running room by copying container files; room state
is not stored there.

## Operations

Useful commands:

```bash
docker compose ps
docker compose logs -f --tail=200 app
docker compose logs -f --tail=200 caddy
docker compose restart app
docker compose pull caddy
docker compose up -d
```

`restart: unless-stopped` recovers a crashed process, but the new process starts
with no rooms. Monitor at least:

- HTTPS health endpoint availability;
- container restart count;
- application error logs;
- disk use for Docker images/logs and Caddy state;
- certificate renewal errors;
- memory and CPU saturation.

The app does not currently expose room count, player count, metrics, or tracing.
If operational visibility is added, expose aggregate counts only and never log
hands or reconnect tokens.

## Reverse proxy requirements

The included Caddy configuration already supports WebSockets through
`reverse_proxy`. If Caddy is replaced, the proxy must:

- forward normal HTTP and WebSocket upgrades;
- preserve HTTPS externally;
- allow long-lived SignalR connections;
- avoid caching `/api/*` and `/hubs/*`;
- serve the app from one stable origin.

Sticky sessions are not a substitute for the single-instance requirement,
because claim timers and room dictionaries are still process-local.

## Security checklist

- Keep HTTPS enabled; reconnect tokens live in browser local storage and travel
  during SignalR resume.
- Restrict SSH and Docker administration to trusted operators.
- Keep the host, Docker, Caddy, and .NET base images patched.
- Do not publish container port 8080 directly to the internet.
- Back up Caddy certificate volumes if desired; no game-data backup exists.
- Treat this as a private/family service. Before opening it broadly, add rate
  limits, abuse controls, structured security logging, and an account model if
  long-lived identity is required.

## Troubleshooting

### Site works but players cannot stay connected

Check Caddy logs, WebSocket proxying, browser developer tools, and any upstream
CDN timeout. The hub path is `/hubs/game`.

### Everyone lost a match

Check `docker compose ps` and the app restart count. A restart or redeploy always
removes in-memory rooms; this is expected, not recoverable.

### A room code no longer exists

The server restarted, or all humans were disconnected past the configured empty
room lifetime.

### Certificate is not issued

Confirm DNS points to the VPS, ports 80/443 are reachable, `DOMAIN` is set, and
read the Caddy logs. Do not replace the public domain with an IP address in the
current automatic-HTTPS setup.

### New UI is not visible

Confirm the new app image is running. Then close/reopen the installed PWA or
clear service-worker/site data on that device.
