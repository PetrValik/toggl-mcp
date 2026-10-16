# Contributing

## Status

This repository is archived and read-only. I am not maintaining it and it accepts no pull
requests or issues. If you want to change something, fork it: a fork is the way to contribute,
and it is meant to be picked up and finished. The code is MIT licensed (see `LICENSE`).

## Build, test, run

You need the .NET 10 SDK.

```bash
dotnet build TogglMcp.slnx
dotnet test TogglMcp.slnx
```

Install your own build as a global tool, which gives you the `toggl-mcp` command:

```bash
dotnet pack src/TogglMcp -c Release -o ./nupkg
dotnet tool install --global TogglMcp --add-source ./nupkg
```

After changing code, re-pack and run `dotnet tool update --global TogglMcp --add-source ./nupkg`.
The update only picks up a new package if `<Version>` in `src/TogglMcp/TogglMcp.csproj` was
bumped; otherwise uninstall and reinstall. The package is not published to NuGet.

To run the hosted Streamable HTTP mode while developing:

```bash
export TOGGL__APITOKEN="<your-toggl-api-token>"
dotnet run --project src/TogglMcp
```

`dotnet run` uses the Development environment, in which auth is off, so `/mcp` is open. Keep
it on localhost. Outside Development the app refuses to start with `AUTH__ENABLED=false`.

To try stdio mode, point an MCP client at the installed `toggl-mcp --stdio`, or at the built
assembly: `dotnet src/TogglMcp/bin/Debug/net10.0/TogglMcp.dll --stdio`. Not at `dotnet run`: it
prints "Building..." to stdout, which the client reads as protocol.

## How the code is organised

One vertical slice per MCP tool under `src/TogglMcp/Features/`. A slice is a thin `*Tool` class
(binds arguments, calls the handler, returns) plus an `internal sealed *Handler`, which is the only
logic layer, plus slice-local request and response records. Slices never reference each other.
Logic two slices need moves down into shared infrastructure next to the code, not sideways:

- `Toggl/` - `ITogglClient` / `TogglClient` (API v9), `TogglThrottle`, models.
- `Tokens/` - `ITogglTokenProvider` and its env-based implementation.
- `Planning/` - `WeekPlan`, `IPlanStore`, `ILedgerStore`, `DuplicateFinder`, `EntryFingerprint`.
- `Mapping/` - the keyword-to-project rule store.
- `Lock/` - `WeekLockPolicy`.
- `Configuration/` - the options classes.
- `Program.cs` - the composition root for both transports.

The full rule-set, written for agents but readable by humans, is
`.claude/skills/mcp-slice-architecture/SKILL.md`. Read it before adding a tool. In particular, only
parameters carrying `[Description]` reach the tool's input schema, and that text is the whole
interface the model sees.

## Invariants a change must keep

These are the product, not obstacles.

- `commit_plan` is the only code path that creates a time entry, and it only replays a plan the
  user approved through `plan_week`. Do not add a second write path or let a read tool write.
- A Toggl token is never a tool argument and never goes through chat. MCP forbids token
  passthrough, because tool arguments transit the model. Tokens come from `ITogglTokenProvider`.
- Entry descriptions are written verbatim. Nothing is appended. Dedup relies on the ledger under
  `STORAGE__DATADIR` plus matching the entry's own content (start instant, duration, project),
  through the one shared `DuplicateFinder`. The legacy `⟨sourceUid⟩` marker is still read for
  old entries and never written.
- When `LOCK__ENABLED=true`, locked weeks are refused, not retried, and the lock is re-checked at
  commit time, not only at plan time.
- In stdio mode stdout is the protocol channel. Nothing but MCP messages may be written there;
  logs go to stderr.

## What is unfinished

Phase 1, a single token from the environment, is what exists. The roadmap terms in code comments
("Phase 2", "Phase 3") refer to the items below.

**Phase 2: identity spike.** Why: the per-user design assumes each person who uses a shared
connector arrives with a distinct `sub` claim. There are reports that connectors added by an
organisation admin collapse everyone into one identity ("last authenticator wins"). Where it
plugs in: `Program.cs` already validates the JWT, and `SubjectOf` and
`EnvTogglTokenProvider.UserKey` read `sub`. Done: two real users connect through a team-added
connector, the server logs the `sub` each request carries, and you have a recorded yes or no.
If the answer is no, do not build the vault.

**Phase 3: per-user encrypted token vault.** Why: today every request uses one shared token,
so a hosted server serves exactly one Toggl account. Where it plugs in:

- A new `ITogglTokenProvider` (`Tokens/`) that returns the token for the current `sub`, replacing
  the registration `AddScoped<ITogglTokenProvider, EnvTogglTokenProvider>()` in `AddTogglMcp`.
- The `/settings` route in `Program.cs`, currently a static stub that is gated by the auth
  policy whenever auth is on, becomes the page where a signed-in user pastes their token, out of band and never via chat.
- ASP.NET Data Protection with the key ring persisted under `STORAGE__DATADIR` and a fixed
  application name; without both, a restart makes every stored token undecryptable.

Done: two users each register a token, tool calls use the right one, the token never appears in
logs or tool arguments, and a restart keeps the vault readable.

**Per-token throttle buckets.** `TogglThrottle` is one process-wide gate spacing requests by
`TOGGL__MINREQUESTINTERVALMS`. Toggl limits per token, so with several users it is conservative.
Done: the throttle keys its gate by token (or `UserKey`), with a test showing two tokens do not
block each other.

**Plans live in memory.** `InMemoryPlanStore` keeps plans for 30 minutes, so a restart drops
pending `planId`s. That is fine for one person (re-run `plan_week`). For a team, decide whether to
persist plans, for example under `STORAGE__DATADIR` as the ledger already is. Done: a restart
between `plan_week` and `commit_plan` either works or is a documented, tested refusal.

**Publishing.** The tool is not on NuGet and there is no published image; both install paths
build from source. Done: a release workflow that packs the tool and pushes it, and/or builds the
Docker image and pushes it to a registry, with a version bump process.

**Optional: team login through a federated identity provider.** Hosted mode needs an OIDC provider
that puts the exact resource URL in the `aud` claim and signs RS256 with a JWKS. A team that
wants login with its existing company accounts can federate that provider upstream. Google
directly does not work (it ignores RFC 8707 resources and has no dynamic client registration).

## Tests

- Handler tests drive the `internal` handlers over a fake Toggl client (`Support/FakeTogglClient.cs`);
  `InternalsVisibleTo` in the csproj exposes them to the test project only.
- File-store tests (ledger, mapping rules) run on a temporary data directory.
- A stdio end-to-end test (`tests/TogglMcp.Tests/Hosting/StdioHostTests.cs`) launches the built
  server with `--stdio` and lists the tools through the MCP C# client.

New behaviour comes with a test, and `dotnet test TogglMcp.slnx` must pass. The tests layout
mirrors the source layout.
