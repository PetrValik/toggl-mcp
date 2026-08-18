# Daily planning: notes → a Toggl day you approve

How the model turns a day's activity notes into a reviewed set of Toggl entries. This is the
"brain" of the tool — the part that saves the time you'd otherwise spend *writing up* what you
did. The server just validates and writes what you approve (`plan_week` → `commit_plan`).

> **This page is the human explanation — the *why*, plus prompts to paste.** The model-facing
> version of the same method is the `toggl-timelog` skill in the
> [claude-kit](https://github.com/PetrValik/Claude-kit) `kit-timelog` plugin (with the `/timelog`
> command). That skill is canonical for how Claude behaves; keep the two in step when the method
> changes, and change the skill first.

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

> Why the guard rails: the výkaz is an official record. "Summarise what I really did, faster" is
> unquestionable. "Pad the numbers to 8 h" is the exact thing a Toggl admin (and anyone above)
> will call fabrication — and it lands on you, not the tool. Rule 3–4 keep you on the right side.

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
5. **Describe** — write one concise, factual description per item — the *výcuc* of what you did,
   straight from the notes (this is the part that eats your time).
6. **Reconcile** — the entries should sum to your real window. If they fall short, **report the
   gap** instead of inventing entries.
7. **Review → commit** — present the day; you edit/drop rows; then `commit_plan` writes it.

For a **multi-day** request, run steps 1–7 independently for each working day in range (its own
window, lunch, meetings and reconcile); skip weekends and days off.

## Ready-to-use prompts (paste into Claude with the connector on)

**Default — one day back (yesterday):**

> Doplň mi do Togglu **včerejšek**. Tady jsou moje poznámky a kalendář za včera; pracovní okno
> 09:00–17:30, oběd 11:15–11:45. Reálné meetingy v jejich časech, oběd mimo (nezapisovat), a moji
> reálnou práci z poznámek rozděl do zbytku okna — poctivé odhady délky a u každé položky jasný
> popis toho, co jsem dělal. **Když práce nepokryje okno, řekni mi, kolik chybí — nic nedoplňuj.**
> Ukaž návrh k odsouhlasení, teprve pak zapíšeme.

**More days — catch up a range / the week:**

> Doplň mi Toggl za {od}–{do} (přeskoč víkendy a dny volna). Pro **každý** pracovní den zvlášť:
> reálné meetingy, oběd mimo, práci z poznámek toho dne rozděl do jeho okna 09:00–17:30 (oběd 11:15–11:45) s
> poctivými odhady a jasným popisem, a u každého dne mi řekni, jestli něco chybí (nedoplňuj).
> Ukaž celý návrh po dnech k odsouhlasení, pak zapíšeme.

## Per-user defaults (later)

Each user is already a separate identity server-side (`UserKey` = the authenticated `sub`), and
mapping rules are stored per user. That same slot is the natural home for a per-user **profile** —
your working window and lunch — so once the team is on it, everyone gets their own defaults and
nobody hard-codes hours into a prompt. Until then, the defaults live in the prompt above.

## What this is deliberately NOT

- Not a way to show 8 h on a day you worked 4 h. The window is real; short day → short report.
- Not an auto-writer — nothing lands in Toggl without your explicit approval.

That honesty *is* the feature: it produces a defensible, accurate write-up of real work in a
fraction of the time — which is the whole reason the tool exists.
