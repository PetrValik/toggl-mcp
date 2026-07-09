# toggl-mcp

A hosted **MCP server** that lets Claude **back-fill Toggl Track time entries** from
whatever record of "what I did" you have (calendar, git, notes, second brain):
**read → propose → you confirm → write.**

It is **one always-on web service**, not an app anyone installs. Claude's cloud calls
it over HTTPS; the server holds the Toggl token(s) and calls the Toggl API v9. Users add
a connector URL in Claude once and log in — nothing runs on their machine.

```
Claude cloud ──HTTPS──▶ https://toggl.irminsul.uk/mcp   (this service, always on)
                          holds Toggl token(s), calls Toggl API v9
You + colleagues: add that URL in Claude once + OAuth login. No install.
```

**Source-agnostic by design:** the server has zero knowledge of where the work record
comes from. Claude reads the source via *its own* connectors and just calls `log_time`.

---

## Status

- **Design:** complete (see the project note in the Obsidian vault).
- **Phase 1 scaffold:** ✅ built and verified — server boots, Streamable HTTP transport
  works, all six tools register, argument binding + validation confirmed via a real MCP
  client. Not yet run against a live Toggl account or deployed.

---

## Stack

- **ASP.NET Core (.NET 10) + `ModelContextProtocol.AspNetCore` 1.4.1**, Streamable HTTP transport.
- One **Docker** container, config via env, behind **Caddy** (cloud/Hetzner) or ALB/App Runner (AWS).
  Same image both places → Hetzner → AWS is a redeploy, not a rewrite.
- Toggl called server-side; token from env (Phase 1) or per-user vault (Phase 3).

## Tools exposed

| Tool | Kind | Purpose |
|---|---|---|
| `list_projects` | read | Active projects in the shared workspace. |
| `list_tasks(projectId)` | read | Tasks under a project (Starter+ feature). |
| `list_entries(from?, to?)` | read | Entries in a date range (default last 7 days). |
| `log_time(...)` | write | Create a **completed** entry. Idempotent per `sourceUid`. |
| `suggest_project(calendarName, title)` | read | Rule match (silent) or fuzzy guess (confirm). |
| `add_mapping_rule(keyword, projectId)` | write | Teach a keyword→project rule. |

Adding a capability later = another `[McpServerTool]` method → instantly live on all devices.

---

## Run Phase 1 locally (just you)

Requires the **.NET 10 SDK**.

```bash
cd src/TogglMcp

# Your raw Toggl token (Toggl → Profile → API Token). NOT a toggl_sk_ key.
export TOGGL__APITOKEN="<your-toggl-api-token>"
# Auth stays off locally → /mcp is open. Local use ONLY.
export AUTH__ENABLED=false

dotnet run
```

Then point any MCP client at `http://localhost:<port>/mcp` (Streamable HTTP). Health
check: `curl http://localhost:<port>/healthz`.

> **Security:** the Toggl token is only ever read from configuration/env or (Phase 3)
> the encrypted vault. It is **never** accepted as a tool argument and never logged —
> MCP forbids token passthrough because tool args transit the model.

### Configuration

All keys bind from `appsettings.json` or env (`__` = nesting). See `.env.example`.

| Env | Default | Meaning |
|---|---|---|
| `TOGGL__APITOKEN` | — | Phase 1 account token (Basic-auth username; password is literal `api_token`). |
| `TOGGL__WORKSPACEID` | *(from `/me`)* | Pin the workspace; else discovered once and cached. |
| `AUTH__ENABLED` | `false` | Turn on JWT validation. **Must be true when public.** |
| `AUTH__AUTHORITY` | — | Authentik OIDC issuer. |
| `AUTH__RESOURCEURL` | — | Public MCP URL; must match JWT `aud` and PRM `resource`. |
| `STORAGE__DATADIR` | `data` | Durable dir (mapping rules; Phase 3 vault + key ring). |

---

## Deploy to `cloud` (Phase 1/3)

1. Copy `.env.example` → `.env`, set `TOGGL__APITOKEN`; for a public deploy also set
   `AUTH__ENABLED=true`, `AUTH__AUTHORITY`, `AUTH__RESOURCEURL=https://toggl.irminsul.uk/mcp`.
2. `docker compose up -d --build` (joins the shared external `web` network).
3. Add `deploy/Caddyfile.snippet` to the cloud Caddyfile and reload Caddy.
4. Verify: `curl https://toggl.irminsul.uk/healthz`.

### Authentik setup checklist (required before `AUTH__ENABLED=true`)

- Authentik **≥ 2025.8** (RFC 8414 metadata).
- An OAuth2/OpenID **Provider** for this app; note its issuer → `AUTH__AUTHORITY`.
- **Scope Mapping bound to the `openid` scope** returning `{"aud": "https://toggl.irminsul.uk/mcp"}`.
  The default `aud` is the client_id, which fails the MCP audience check.
- Assign an **RSA signing key** to the provider (else it signs HS256, not JWKS-verifiable).
- Phase 3 team login: federate Authentik → company **Google Workspace** (Directory →
  Federation → Google source; "Link on unique identifier"; enrollment flow
  `default-source-enrollment`; gate the domain via Google "Internal" consent + the `hd` claim).

> **Why Authentik and not Google directly:** Google ignores the RFC 8707 `resource`
> param, issues opaque tokens with `aud = client_id`, and has no Dynamic Client
> Registration — so the MCP-mandated audience check is impossible. Brokering through
> Authentik is the correct path.

---

## Connect in Claude

Add a **custom connector** with URL `https://toggl.irminsul.uk/mcp` and complete the
OAuth login. The C# SDK auto-serves the RFC 9728 Protected Resource Metadata at
`/.well-known/oauth-protected-resource` and the `401 WWW-Authenticate` challenge, so
Claude discovers the Authentik authorization server automatically.

---

## Phased plan

1. **Phase 1 — cloud, just me:** env token, `AUTH__ENABLED=false` locally then `true`
   with Authentik locked to me. Validates the whole Toggl + Authentik + connector chain.
2. **Phase 2 — identity spike:** 2 devs connect on Claude Team; the server logs the `sub`
   it sees. **Distinct → per-user vault is safe. Same → stop before building it.** This is
   the one live risk: org-admin-added connectors have credible reports of collapsing to a
   single shared token ("last authenticator wins").
3. **Phase 3 — team on AWS:** switch on the per-user encrypted vault (registered via
   `/settings`, keyed by Authentik `sub`) + Google-federated login; redeploy the same image
   to company AWS + subdomain; publish self-host docs.

## Gotchas baked into the code (don't undo them)

- **Toggl Basic auth field order:** token is the *username*, literal `api_token` the password.
- **Idempotency:** Toggl has no idempotency key — `log_time` embeds `⟨sourceUid⟩` in the
  description and checks the day first. Always pass `sourceUid` or re-runs duplicate.
- **DST:** `start` is passed through verbatim as RFC3339 with the source's own offset —
  never hardcode `+02:00`.
- **JWT audience:** Authentik's default `aud` is the client_id → add the `aud` scope mapping.
- **Data Protection key ring (Phase 3):** must be persisted to `STORAGE__DATADIR` with a
  fixed application name, or a restart makes every stored token undecryptable.
- **`MapMcp` path:** pinned to `/mcp` so it byte-matches the PRM `resource`.

---

## Layout

```
src/TogglMcp/
  Program.cs              MCP + HTTP transport + auth toggle wiring
  Configuration/          TogglOptions, AuthOptions, StorageOptions
  Toggl/                  ITogglClient/TogglClient (v9), throttle, models, exception
  Tokens/                 ITogglTokenProvider (env now; vault in Phase 3)
  Mapping/                keyword→project rule store (JSON per user)
  Mcp/                    TogglTools (the six tools) + output DTOs
Dockerfile · docker-compose.yml · deploy/Caddyfile.snippet · .env.example
```
