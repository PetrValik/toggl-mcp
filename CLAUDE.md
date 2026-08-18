# CLAUDE.md — toggl-mcp

An MCP server that back-fills a Toggl Track timesheet from a plan the user has approved.
Deployed at `https://toggl.irminsul.uk/mcp`; `.claude/kit.profile.yaml` holds the build/test
commands, branch policy and risk paths — read it rather than guessing.

---

## Skills — REQUIRED before writing code

**You MUST invoke the Skill tool for the matching skill below before doing the work — even for a
one-line answer.** Loading the rule-set after the code is written is not routing.

<!-- BEGIN:kit-skills -->
| When you are about to…                                        | Invoke                     |
| -------------------------------------------------------------- | -------------------------- |
| add or change anything under `src/TogglMcp/Features/`         | `mcp-slice-architecture`   |
<!-- END:kit-skills -->

| When you are about to…                                        | Invoke                     |
| -------------------------------------------------------------- | -------------------------- |
| write tests for a slice                                        | `slice-testing`            |
| branch, commit, push, or open a PR                             | `conventions`              |
| read or change `.claude/kit.profile.yaml`                      | `profile-contract`         |
| record finished work in the second brain                       | `second-brain`             |

`mcp-slice-architecture` is **this repo's own** skill (`.claude/skills/`). It is the delta from
the generic `vertical-slice-architecture-dotnet` pack, which describes a web stack this server
does not have. Where the two disagree, the repo skill wins. It is also gated: the first edit
under `Features/` is denied until it has been invoked.

---

## Facts that hold in every session

**This writes to an official timesheet.** `commit_plan` is the only tool that creates a time
entry, and only by replaying a plan the user approved. Do not add a second write path, and do not
widen a read tool into a writing one — the review gate is the product, not an obstacle.

**Never route a Toggl token through chat or a tool argument.** MCP forbids token passthrough
(tool args transit the model). Tokens come from `ITogglTokenProvider`, sourced from env/config.
When a token has to be set on the box, hand the user the command and let them run it.

**Entry descriptions are written verbatim.** Nothing is appended to what the user approved —
dedup identity lives in the ledger under `StorageOptions.DataDir` and in the entry's own content.
The legacy `⟨sourceUid⟩` marker is still *read* for entries written before 2026-08-18, never
written. See `README.md` → *Gotchas baked into the code*.

**Weeks lock permanently** (Friday 23:59 Europe/Prague, the week after). A locked week is refused,
not retried; only the workspace admin can reopen one.

**Deploying is pushing.** A push to `main` runs `.github/workflows/deploy.yml`, which rsyncs to
the Hetzner box and restarts the container. Work on a branch and merge deliberately — the kit's
trunk guard enforces this. The workflow gates on `dotnet test`.

**Durable state lives on a volume** (`/data` in the container, `STORAGE__DATADIR`): mapping rules
and the commit ledger today, the encrypted token vault and Data Protection key ring in Phase 3.
Losing it is not fatal but the server forgets what it has already written.

---

## Language

Repo artefacts — code, comments, docs, commit messages — are English. Chat with the author may be
Czech.

## Docs

- `README.md` — install, deploy, architecture, and the gotchas list.
- `docs/USAGE.md` — the tool surface, statuses, idempotency, troubleshooting.
- `docs/DAILY-PLANNING.md` — the method the model follows; the model-facing version is the
  `toggl-timelog` skill in the `kit-timelog` plugin.
- `deploy/DEPLOY.md` — the one-time box setup.
