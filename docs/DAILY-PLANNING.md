# Daily planning: notes → a Toggl day you approve

How the model turns a day's activity notes into a reviewed set of Toggl entries. This is the
"brain" of the tool — the part that saves the time you'd otherwise spend *writing up* what you
did. The server just validates and writes what you approve (`plan_week` → `commit_plan`).

> **This page is the canonical description of the method** — the *why*, plus prompts to paste.
> Point your model at it (or paste the parts you need) and it will follow the same steps.

## Scope: one day back (default), or more

**Default = a single day, one day back** (yesterday's working day) — the everyday case: a quick
"write up yesterday" you approve in seconds. You can also aim it at **today** (e.g. at the end of
your day) or at **several days / a whole week** to catch up. For a range the model just repeats
the per-day logic below for each working day — each with its own window, lunch, meetings and its
own reconcile step. Weekends and days off are skipped, never filled.

## Principles (read first — these keep it honest and defensible)

1. **Only real work.** Every entry maps to something you actually did — from your post-activity
   notes or your calendar. No invented tasks, ever.
2. **Durations are honest estimates.** You don't stopwatch; the model estimates the real split.
   That's fine — an estimate of real work is not a fabrication.
3. **The day equals your real working window.** You define `start`, `end`, and `lunch`. The tool
   allocates *that real window* across your real activities — it divides a day you actually
   worked; it does **not** manufacture hours to hit a target. Worked a short day → report a short
   day.
4. **Gaps are surfaced, not padded.** If your notes don't account for the whole window, the model
   tells you the gap ("~1h30 unaccounted") so you add a note or adjust the window. It never
   invents filler entries to reach 8 h.
5. **You review every entry before it's written.** `commit_plan` only replays what you approved.

> Why the guard rails: the timesheet is an official record. "Summarise what I really did, faster" is
> unquestionable. "Pad the numbers to 8 h" is the exact thing a Toggl admin (and anyone above)
> will call fabrication — and it lands on you, not the tool. Rules 3 and 4 keep you on the right side.

## Inputs each day

| Input | Example | Notes |
|---|---|---|
| Working window | `09:00–17:30` | The real hours you were working. This is the total the day fills. |
| Lunch | `11:15–11:45` | Preset; excluded from worked time (not a Toggl entry). |
| Meetings | from calendar | Real meetings at their real times. |
| Activity notes | your notes | What you worked on (project/task + a line on what you did). |

Set your usual window + lunch once as defaults; override on days that differ (left early, half day).
Current default: **09:00–17:30, lunch 11:15–11:45 → exactly 8.0 h worked**, so a full day lands on
8 h with no padding needed.

## How the model builds the day

1. **Meetings first** — place real meetings at their fixed calendar times.
2. **Lunch** — carve out the preset lunch (not worked, not logged).
3. **Real work** — extract the actual work items from your notes; group by project/task.
4. **Allocate** — split the remaining window (`window − lunch − meetings`) across those real items,
   proportional to effort, with honest duration estimates.
5. **Describe** — write one concise, factual description per item — the gist of what you did,
   straight from the notes (this is the part that eats your time).
6. **Reconcile** — the entries should sum to your real window. If they fall short, **report the
   gap** instead of inventing entries.
7. **Review → commit** — present the day; you edit/drop rows; then `commit_plan` writes it.

For a **multi-day** request, run steps 1–7 independently for each working day in range (its own
window, lunch, meetings and reconcile); skip weekends and days off.

## Ready-to-use prompts (paste into Claude with toggl-mcp connected)

**Default — one day back (yesterday):**

> Fill in my Toggl for **yesterday**. Here are my notes and calendar for it; working window
> 09:00–17:30, lunch 11:15–11:45. Put real meetings at their real times, leave lunch out (don't
> log it), and split my real work from the notes across the rest of the window, with honest
> duration estimates and a clear description of what I did for each item. **If the work doesn't
> cover the window, tell me how much is missing — don't fill it.** Show me the proposal to
> approve, and only then write it.

**More days — catch up a range / the week:**

> Fill in my Toggl for {from}–{to} (skip weekends and days off). For **each** working day
> separately: real meetings, lunch left out, and that day's work from the notes split across its
> 09:00–17:30 window (lunch 11:15–11:45) with honest estimates and a clear description, and for
> each day tell me whether anything is missing (don't fill it). Show the whole proposal day by
> day for approval, then write it.

## Per-user defaults (not built)

Today the defaults live in the prompt above. The roadmap's Phase 3 would key a per-user profile
(working window, lunch) and a token vault on the OIDC `sub` in hosted mode; that was never built.
Mapping rules are stored under `STORAGE__DATADIR`, not in a profile.

## What this is deliberately NOT

- Not a way to show 8 h on a day you worked 4 h. The window is real; short day → short report.
- Not an auto-writer — nothing lands in Toggl without your explicit approval.

That honesty *is* the feature: it produces a defensible, accurate write-up of real work in a
fraction of the time — which is the whole reason the tool exists.
