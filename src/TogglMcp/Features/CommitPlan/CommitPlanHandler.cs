using System.Collections.Concurrent;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.CommitPlan;

internal sealed class CommitPlanHandler(
    ITogglClient toggl,
    ITogglTokenProvider tokens,
    WeekLockPolicy locks,
    IPlanStore plans)
{
    // Per-(user, plan) gate so concurrent/retried commits can't both write.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CommitLocks = new();

    public async Task<CommitResult> HandleAsync(string planId, string[]? exclude, CancellationToken cancellationToken)
    {
        var plan = plans.Get(tokens.UserKey, planId);
        if (plan is null)
            return new CommitResult(planId, 0, 0, 0, [], "Plan not found or expired - run plan_week again.");

        var excludeSet = new HashSet<string>(exclude ?? [], StringComparer.Ordinal);
        var toWrite = plan.Entries.Where(e => e.Status == PlannedEntry.StatusNew && !excludeSet.Contains(e.SourceUid)).ToList();

        var results = new List<CommitEntryResult>();
        int created = 0, skipped = 0, failed = 0;

        // Serialise all commits of this plan so concurrent/retried calls can't both write
        // (Toggl has no idempotency key; a marker re-check alone can't close a TOCTOU race).
        var gate = CommitLocks.GetOrAdd($"{tokens.UserKey}:{planId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wid = await toggl.GetWorkspaceIdAsync(cancellationToken);
            var dayCache = new Dictionary<DateOnly, IReadOnlyList<TogglTimeEntry>>();
            var writtenMarkers = new HashSet<string>(StringComparer.Ordinal); // markers created in this run

            foreach (var entry in toWrite)
            {
                if (!locks.IsEditable(entry.Day))
                {
                    results.Add(new CommitEntryResult(entry.SourceUid, "skipped", $"week locked since planning; ask {locks.AdminContact} to reopen it", null));
                    skipped++;
                    continue;
                }

                var marker = SourceMarker.For(entry.SourceUid);

                // Re-dedup against Toggl (one fetch per day, cached) — catches a re-commit after
                // the entries already landed.
                if (!dayCache.TryGetValue(entry.Day, out var dayEntries))
                {
                    dayEntries = await toggl.GetTimeEntriesAsync(entry.Day, entry.Day.AddDays(1), cancellationToken);
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

                try
                {
                    var written = await toggl.CreateTimeEntryAsync(new CreateTimeEntryRequest
                    {
                        CreatedWith = "toggl-mcp",
                        WorkspaceId = wid,
                        Start = entry.Start,
                        Duration = entry.DurationSeconds,
                        Description = entry.Description,
                        ProjectId = entry.ProjectId,
                        TaskId = entry.TaskId,
                        Tags = entry.Tags is { Count: > 0 } ? entry.Tags : null,
                    }, cancellationToken);
                    results.Add(new CommitEntryResult(entry.SourceUid, "created", $"entry {written.Id} on {entry.Day:yyyy-MM-dd}", written.Id));
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

        var excluded = plan.Entries.Count(e => e.Status == PlannedEntry.StatusNew && excludeSet.Contains(e.SourceUid));
        var summary = $"Committed {created}, skipped {skipped}, failed {failed}" + (excluded > 0 ? $"; {excluded} excluded by you." : ".");
        return new CommitResult(planId, created, skipped, failed, results, summary);
    }
}
