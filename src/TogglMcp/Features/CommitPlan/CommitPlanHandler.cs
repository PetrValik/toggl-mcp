using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.CommitPlan;

/// <summary>
/// The only logic layer of the commit_plan slice: replays a frozen <see cref="WeekPlan"/>'s "new"
/// entries into Toggl, re-checking the week lock and deduping at write time. Sealed
/// and internal — reachable only through <see cref="CommitPlanTool"/>.
/// </summary>
internal sealed class CommitPlanHandler(
    ITogglClient toggl,
    ITogglTokenProvider tokens,
    WeekLockPolicy locks,
    IPlanStore plans,
    ILedgerStore ledger,
    IOptions<TogglOptions> togglOptions)
{
    // Per-user commit gate: serialises a user's commit_plan calls so concurrent/retried ones can't
    // double-write. Keyed by UserKey (not planId) so the dictionary is bounded by the user
    // population and never leaks one SemaphoreSlim per committed plan for the process lifetime.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CommitLocks = new();

    /// <summary>
    /// Commits the entries of <paramref name="planId"/>, skipping any whose sourceUid is in
    /// <paramref name="exclude"/>. Idempotent: locked days and already-logged entries are skipped,
    /// not re-written.
    /// </summary>
    /// <param name="planId">Id of a plan previously frozen by plan_week.</param>
    /// <param name="exclude">sourceUids the user chose not to log; null = commit everything.</param>
    /// <returns>Per-entry outcomes plus created/skipped/failed counts.</returns>
    public async Task<CommitResult> HandleAsync(string planId, string[]? exclude, CancellationToken cancellationToken)
    {
        // Plan store is TTL'd, so a stale/expired planId reads back null — bail with guidance.
        var plan = plans.Get(tokens.UserKey, planId);
        if (plan is null)
            return new CommitResult(planId, 0, 0, 0, [], "Plan not found or expired — run plan_week again.");

        // Only ever write entries the plan froze as "new" (never "existing"/"conflict") and that
        // the caller didn't exclude — a verbatim replay of what the user approved.
        var excludeSet = new HashSet<string>(exclude ?? [], StringComparer.Ordinal);
        var toWrite = plan.Entries.Where(e => e.Status == PlannedEntry.StatusNew && !excludeSet.Contains(e.SourceUid)).ToList();

        var results = new List<CommitEntryResult>();
        int created = 0, skipped = 0, failed = 0;

        // Nothing left to write (everything excluded, or the plan held no new entries): don't take
        // the gate or call Toggl at all.
        if (toWrite.Count == 0)
            return new CommitResult(planId, 0, 0, 0, [], SummaryOf(plan, excludeSet, 0, 0, 0));

        // Serialise this user's commits so concurrent/retried calls can't both write
        // (Toggl has no idempotency key; a dedup re-check alone can't close a TOCTOU race).
        var gate = CommitLocks.GetOrAdd(tokens.UserKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wid = await toggl.GetWorkspaceIdAsync(cancellationToken);

            // One read of the truth for the whole run: Toggl's entries across the days being
            // written, plus what this server remembers writing. Widened ±1 day because Toggl may
            // bucket by UTC, so an entry starting 00:00–02:00 Europe/Prague sits on the previous
            // UTC date; identity matching keeps the wider window false-positive-free.
            var minDay = toWrite.Min(e => e.Day);
            var maxDay = toWrite.Max(e => e.Day);
            var existing = await toggl.GetTimeEntriesAsync(minDay.AddDays(-1), maxDay.AddDays(2), cancellationToken);
            var recorded = await ledger.GetAsync(tokens.UserKey, minDay.AddDays(-1), maxDay.AddDays(1), cancellationToken);
            var duplicates = new DuplicateFinder(existing, recorded);

            foreach (var entry in toWrite)
            {
                // Re-check the lock now, not at plan time: a week may have locked between plan and commit.
                if (!locks.IsEditable(entry.Day))
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "skipped", $"week locked since planning; ask {locks.AdminContact} to reopen it", null));
                    skipped++;
                    continue;
                }

                // Re-dedup immediately before writing — catches a re-commit after the entries
                // already landed, an entry logged by hand in the meantime, and anything this run
                // wrote a moment ago (the fetched list is a pre-write snapshot).
                var dup = duplicates.Find(entry.SourceUid, entry.Start, entry.DurationSeconds, entry.ProjectId);
                if (dup is not null)
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "duplicate", dup.Reason, dup.EntryId));
                    skipped++;
                    continue;
                }

                // Passed all dedup checks — actually write it to Toggl.
                try
                {
                    var written = await toggl.CreateTimeEntryAsync(new CreateTimeEntryRequest
                    {
                        CreatedWith = togglOptions.Value.CreatedWith,
                        WorkspaceId = wid,
                        Start = entry.Start,
                        Duration = entry.DurationSeconds,
                        Description = entry.Description,
                        ProjectId = entry.ProjectId,
                        TaskId = entry.TaskId,
                        Tags = entry.Tags is { Count: > 0 } ? entry.Tags : null,
                    }, cancellationToken);
                    // It exists now: nothing later in this run may write it again...
                    duplicates.Remember(entry.SourceUid, written.Id, entry.Start, entry.DurationSeconds, entry.ProjectId);
                    // ...and no later run may either. The entry is already in Toggl by this point,
                    // so a ledger that can't be written is degraded, not fatal: content matching
                    // still finds the entry. Report the create, say what was lost, keep going.
                    var ledgerNote = "";
                    try
                    {
                        await ledger.RecordAsync(tokens.UserKey, entry.SourceUid, written.Id, entry.Day, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        ledgerNote = "; not recorded in the ledger — dedup falls back to matching on content";
                    }
                    results.Add(new CommitEntryResult(entry.SourceUid, "created", $"entry {written.Id} on {entry.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{ledgerNote}", written.Id));
                    created++;
                }
                catch (TogglException ex)
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "error", ex.Message, null));
                    failed++;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        return new CommitResult(planId, created, skipped, failed, results, SummaryOf(plan, excludeSet, created, skipped, failed));
    }

    // Counts entries the user deliberately excluded (only "new" ones could have been written) so
    // the summary can distinguish "skipped by us" from "excluded by you".
    private static string SummaryOf(WeekPlan plan, HashSet<string> excludeSet, int created, int skipped, int failed)
    {
        var excluded = plan.Entries.Count(e => e.Status == PlannedEntry.StatusNew && excludeSet.Contains(e.SourceUid));
        return $"Committed {created}, skipped {skipped}, failed {failed}" + (excluded > 0 ? $"; {excluded} excluded by you." : ".");
    }
}
