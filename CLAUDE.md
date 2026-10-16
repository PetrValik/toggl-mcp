# CLAUDE.md — toggl-mcp

An MCP server that back-fills a Toggl Track timesheet from a plan the user has approved. Archived
upstream (October 2026) and meant to be forked: it runs locally over stdio (`toggl-mcp --stdio`)
or self-hosted over Streamable HTTP behind OIDC. Build: `dotnet build TogglMcp.slnx`. Test:
`dotnet test TogglMcp.slnx`.

---

## Skills — REQUIRED before writing code

**You MUST invoke the Skill tool for the matching skill below before doing the work — even for a
one-line answer.** Loading the rule-set after the code is written is not routing.

<!-- BEGIN:kit-skills -->
| When you are about to…                                        | Invoke                     |
| -------------------------------------------------------------- | -------------------------- |
| add or change anything under `src/TogglMcp/Features/`         | `mcp-slice-architecture`   |
<!-- END:kit-skills -->

`mcp-slice-architecture` is **this repo's own** skill (`.claude/skills/`). It is the delta from
the generic Vertical Slice Architecture for .NET, which describes a web stack this server does not
have. Where the two disagree, the repo skill wins. `.claude/kit.profile.yaml` is read only by the
optional claude-kit plugin, which also gates the first edit under `Features/` on that skill.

---

## Facts that hold in every session

**This writes to an official timesheet.** `commit_plan` is the only tool that creates a time
entry, and only by replaying a plan the user approved. Do not add a second write path, and do not
widen a read tool into a writing one — the review gate is the product, not an obstacle.

**Never route a Toggl token through chat or a tool argument.** MCP forbids token passthrough
(tool args transit the model). Tokens come from `ITogglTokenProvider`, sourced from env/config.
When a token has to be set somewhere, hand the user the command and let them run it.

**Entry descriptions are written verbatim.** Nothing is appended to what the user approved —
dedup identity lives in the ledger under `StorageOptions.DataDir` and in the entry's own content.
The legacy `⟨sourceUid⟩` marker is still *read* for entries written before 2026-08-18, never
written. See `README.md` → *Gotchas baked into the code*.

**In stdio mode, stdout is the protocol.** Logging goes to stderr; a single stray write to
`Console.Out` breaks the client's handshake. `tests/TogglMcp.Tests/Hosting/StdioHostTests.cs`
drives the real process and catches it.

**The week lock is opt-in** (`LOCK__ENABLED`). When on, a locked week is refused, not retried;
only the workspace admin can reopen one. `LOCK__TIMEZONE` defines day and week boundaries even
with the lock off (blank = the machine's zone).

**Durable state lives in `STORAGE__DATADIR`**: mapping rules and the commit ledger today, the
encrypted token vault and Data Protection key ring in Phase 3. Under stdio a relative path
resolves into the user's local app-data folder; in the container it is the `/data` volume.
Losing it is not fatal but the server forgets what it has already written.

**Nothing deploys on push.** The only workflow (`.github/workflows/ci.yml`) builds and tests.

---

## Language

Repo artefacts — code, comments, docs, commit messages — are English.

## Docs

- `README.md` — install, configuration, architecture, and the gotchas list.
- `CONTRIBUTING.md` — build/test, the invariants, and the unfinished roadmap (Phase 2/3).
- `docs/USAGE.md` — the tool surface, statuses, idempotency, troubleshooting.
- `docs/DAILY-PLANNING.md` — the method the model follows when turning notes into a day.
- `deploy/DEPLOY.md` — self-hosting the HTTP mode behind a reverse proxy and an OIDC provider.
