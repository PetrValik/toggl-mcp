# Self-hosting the HTTP mode

This is a worked example of running the hosted Streamable HTTP mode: one Docker container behind
Caddy, authenticated by an OIDC provider. If you only need the server for yourself, the local
`--stdio` mode in the README is simpler and needs none of this.

There is no deploy automation in the repo. The original GitHub Actions workflow was removed when
the project was archived; a fork can add its own (rsync or a registry image, then
`docker compose up -d --build`, gated on the container healthcheck).

## What the OIDC provider must do

Any provider works if it:

1. puts the exact MCP resource URL in the access token `aud` claim (RFC 8707 resource indicators), and
2. signs tokens with RS256 and publishes a JWKS.

Google directly does not work: it ignores the `resource` parameter, sets `aud` to the client id, and
has no dynamic client registration. Put an OIDC broker in front if you need Google logins.

### Tested example: Authentik >= 2025.8

- Create an OAuth2/OpenID provider and an application for it. Its issuer looks like
  `https://auth.example.com/application/o/toggl-mcp/`; that is `AUTH__AUTHORITY`.
- Add a scope mapping on the `openid` scope that returns
  `{"aud": "https://toggl-mcp.example.com/mcp"}` (your exact resource URL).
- Select an RSA signing key on the provider.
- The original deployment used a public client with PKCE.

## Setup

### 1. DNS and reverse proxy
Point `toggl-mcp.example.com` at your server and add `deploy/Caddyfile.snippet` to your Caddyfile.
The compose file publishes the container on `127.0.0.1:8080` only, which a Caddy on the same host
reaches directly. If your proxy runs in a container, attach `toggl-mcp` to the Docker network your
reverse proxy uses and point the upstream at `toggl-mcp:8080` instead.

### 2. Configuration
```bash
cp .env.example .env
```
Fill in at least:
```env
TOGGL__APITOKEN=<your-toggl-token>
AUTH__ENABLED=true
AUTH__AUTHORITY=https://auth.example.com/application/o/toggl-mcp/
AUTH__RESOURCEURL=https://toggl-mcp.example.com/mcp
STORAGE__DATADIR=/data
ASPNETCORE_URLS=http://+:8080
```
`AUTH__RESOURCEURL` must byte-match the token `aud` and the Protected Resource Metadata `resource`.
Containers run in UTC, so also set `LOCK__TIMEZONE` (for example `Europe/Prague`) or week and day
boundaries will be computed in UTC. Optional: `AUTH__ALLOWEDSUBJECTS__0` to restrict who may call
the server, `AUTH__REQUIREDSCOPE` to require a scope.

### 3. Start
```bash
docker compose up -d --build
docker compose ps          # wait for "healthy"
curl https://toggl-mcp.example.com/healthz
```

### 4. Connect a client
Add `https://toggl-mcp.example.com/mcp` as a remote MCP server in your client. It discovers the
provider through `/.well-known/oauth-protected-resource` and logs you in.

## Notes
- The mapping rules and commit ledger live in the `toggl-mcp-data` volume (`/data`; compose prefixes it with the project name, e.g.
  `toggl-mcp_toggl-mcp-data`). Losing it is
  not fatal, but the server forgets what it already wrote and dedup falls back to content matching.
- The app refuses to start with `AUTH__ENABLED=false` outside the Development environment.
- Every tool call uses the one token in `TOGGL__APITOKEN`. Per-user tokens were planned (roadmap
  Phase 3, see CONTRIBUTING.md) and never built, so do not share a hosted instance between people
  with different Toggl accounts.
- Rolling back: check out the older revision and `docker compose up -d --build`.
