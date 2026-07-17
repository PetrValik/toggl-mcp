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
/// entries into Toggl, re-checking the week lock and deduping (marker-based) at write time. Sealed
/// and internal — reachable only through <see cref="CommitPlanTool"/>.
/// </summary>
internal sealed class CommitPlanHandler(
    ITogglClient toggl,
    ITogglTokenProvider tokens,
    WeekLockPolicy locks,
    IPlanStore plans,
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

        // Serialise this user's commits so concurrent/retried calls can't both write
        // (Toggl has no idempotency key; a marker re-check alone can't close a TOCTOU race).
        var gate = CommitLocks.GetOrAdd(tokens.UserKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wid = await toggl.GetWorkspaceIdAsync(cancellationToken);
            // One existing-entries fetch per day, reused across entries sharing that day.
            var dayCache = new Dictionary<DateOnly, IReadOnlyList<TogglTimeEntry>>();
            var writtenMarkers = new HashSet<string>(StringComparer.Ordinal); // markers created in this run

            foreach (var entry in toWrite)
            {
                // Re-check the lock now, not at plan time: a week may have locked between plan and commit.
                if (!locks.IsEditable(entry.Day))
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "skipped", $"week locked since planning; ask {locks.AdminContact} to reopen it", null));
                    skipped++;
                    continue;
                }

                var marker = SourceMarker.For(entry.SourceUid);

                // Re-dedup against Toggl (one fetch per day, cached) — catches a re-commit after
                // the entries already landed. Fetch ±1 day around the local day: Toggl's
                // start_date/end_date filtering may bucket by UTC, so an entry starting
                // 00:00–02:00 Europe/Prague would fall on the previous UTC date and escape an
                // exact-day window. The marker match makes the wider window false-positive-free.
                if (!dayCache.TryGetValue(entry.Day, out var dayEntries))
                {
                    dayEntries = await toggl.GetTimeEntriesAsync(entry.Day.AddDays(-1), entry.Day.AddDays(2), cancellationToken);
                    dayCache[entry.Day] = dayEntries;
                }
                var dup = dayEntries.FirstOrDefault(x => x.Description?.Contains(marker, StringComparison.Ordinal) == true);
                if (dup is not null)
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "duplicate", $"already logged (entry {dup.Id})", dup.Id));
                    skipped++;
                    continue;
                }

                // Same marker already written earlier in THIS run (the cached day list is a
                // pre-write snapshot, so it wouldn't show it).
                if (!writtenMarkers.Add(marker))
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "duplicate", "already written in this commit run", null));
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
                    results.Add(new CommitEntryResult(entry.SourceUid, "created", $"entry {written.Id} on {entry.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", written.Id));
                    created++;
                }
                catch (TogglException ex)
                {
                    writtenMarkers.Remove(marker); // write failed — don't block a genuine retry
                    results.Add(new CommitEntryResult(entry.SourceUid, "error", ex.Message, null));
                    failed++;
                }
            }
        }
        finally
        {
            gate.Release();
        }

        // Count entries the user deliberately excluded (only "new" ones could have been written)
        // so the summary can distinguish "skipped by us" from "excluded by you".
        var excluded = plan.Entries.Count(e => e.Status == PlannedEntry.StatusNew && excludeSet.Contains(e.SourceUid));
        var summary = $"Committed {created}, skipped {skipped}, failed {failed}" + (excluded > 0 ? $"; {excluded} excluded by you." : ".");
        return new CommitResult(planId, created, skipped, failed, results, summary);
    }
}
