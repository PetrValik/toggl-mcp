using System.Globalization;
using System.Text.RegularExpressions;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;
using static System.FormattableString;

namespace TogglMcp.Features.PlanWeek;

/// <summary>
/// The sole logic layer of the PlanWeek slice. Turns proposed entries into a validated,
/// project-resolved, deduped, lock-checked preview and — if anything is writable — freezes it
/// as a <see cref="WeekPlan"/> keyed by a planId for a later commit_plan. Writes nothing to
/// Toggl itself; it only reads projects and existing entries to classify each proposal.
/// </summary>
internal sealed class PlanWeekHandler(
    ITogglClient toggl,                 // Toggl API reads: projects + existing entries for dedup
    ITogglTokenProvider tokens,         // supplies UserKey stamped onto the frozen plan
    WeekLockPolicy locks,               // day/week -> editable? and lock deadlines
    IPlanStore plans,                   // where the frozen WeekPlan is saved under its planId
    ILedgerStore ledger,                // what this server has already written, per user
    TimeProvider clock)                 // injectable now() for the plan's CreatedAt
{
    // RFC3339 explicit offset: trailing 'Z' or ±HH[:]MM / ±HH.
    private static readonly Regex OffsetSuffix = new(@"(?:[Zz]|[+-]\d{2}(?::?\d{2})?)$", RegexOptions.Compiled);

    // Guard rails against oversized/absurd input: the whole classified batch is frozen for the plan
    // TTL, and resolution/dedup are O(entries·projects)/O(entries·existing); a wild date would also
    // blow up the existing-entries fetch window.
    private const int MaxEntries = 200;
    private const int MaxDaysFromToday = 366;

    /// <summary>
    /// Classifies each proposed entry (new / duplicate / locked / invalid) and, when at least one
    /// is writable, persists a frozen plan and returns its planId. Deliberately makes no writes to
    /// Toggl — commit_plan is the write step.
    /// </summary>
    /// <param name="entries">Proposals for the week's empty working days; may be empty/null.</param>
    /// <returns>A preview with per-entry verdicts, counts, week/lock info, and (if writable) a planId.</returns>
    public async Task<PlanResult> HandleAsync(PlanEntryInput[] entries, CancellationToken cancellationToken)
    {
        // Nothing proposed -> empty preview, no plan, no Toggl calls.
        if (entries is null || entries.Length == 0)
            return new PlanResult(null, "", false, null, 0, 0, 0, [], "No entries proposed.");

        // Reject oversized batches before doing any work (memory + CPU + plan-store footprint).
        if (entries.Length > MaxEntries)
            return new PlanResult(null, "", false, null, 0, 0, 0, [],
                $"Too many entries ({entries.Length}); plan at most {MaxEntries} at once.");

        var today = locks.Today();

        // First pass: parse + validate shape — no Toggl calls yet.
        var parsed = entries.Select(entry =>
        {
            string? error = null;
            DateOnly? day = null;
            if (string.IsNullOrWhiteSpace(entry.SourceUid)) error = "sourceUid is required.";
            else if (entry.DurationSeconds <= 0) error = "durationSeconds must be positive.";
            else if (entry.TaskId is not null && entry.ProjectId is null && string.IsNullOrWhiteSpace(entry.ProjectName)) error = "taskId requires a project.";
            else if (!DateTimeOffset.TryParse(entry.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) || !HasExplicitOffset(entry.Start))
                error = $"start '{entry.Start}' must be RFC3339 with an explicit offset (e.g. 2026-07-13T09:00:00+02:00).";
            else
            {
                var localDay = locks.LocalDayOf(dto);
                // Reject dates absurdly far from today so one entry can't force a giant Toggl fetch window.
                if (Math.Abs(localDay.DayNumber - today.DayNumber) > MaxDaysFromToday)
                    error = $"start '{entry.Start}' is outside the supported ±{MaxDaysFromToday}-day window.";
                else day = localDay;
            }
            return (In: entry, Day: day, Error: error);
        }).ToList();

        // If nothing is even shape-valid, return the invalids without touching Toggl.
        if (parsed.All(p => p.Error is not null))
        {
            var invalids = parsed.Select(p => ToDto(Invalid(p.In, p.Day, p.Error!))).ToList();
            return new PlanResult(null, "", false, null, 0, 0, invalids.Count, invalids,
                $"All {invalids.Count} proposed entr{(invalids.Count == 1 ? "y is" : "ies are")} invalid; nothing to plan.");
        }

        // At least one entry is shape-valid: now hit Toggl. Projects for id/name resolution...
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var byId = projects.ToDictionary(project => project.Id);

        // ...and existing entries across the spanned day range for the dedup check. The range is
        // widened ±1 day: Toggl's start_date/end_date filtering may bucket by UTC, so an entry
        // starting 00:00–02:00 Europe/Prague sits on the previous UTC date and would escape an
        // exact window. Matching on identity (ledger/fingerprint/legacy marker) rather than on the
        // window itself keeps the wider range false-positive-free.
        var validDays = parsed.Where(p => p.Day is not null).Select(p => p.Day!.Value).ToList();
        var minDay = validDays.Count > 0 ? validDays.Min() : locks.Today();
        var maxDay = validDays.Count > 0 ? validDays.Max() : locks.Today();
        var existing = await toggl.GetTimeEntriesAsync(minDay.AddDays(-1), maxDay.AddDays(2), cancellationToken);
        var recorded = await ledger.GetAsync(tokens.UserKey, minDay.AddDays(-1), maxDay.AddDays(1), cancellationToken);
        var duplicates = new DuplicateFinder(existing, recorded);

        // Second pass: resolve project, check lock + dedup, and assign each entry a final status.
        var planned = new List<PlannedEntry>();
        // Dedup within this batch: two proposals must not both be planned when they carry the same
        // sourceUid, or describe the same slot of the day, since neither is in Toggl yet.
        var claimedUids = new HashSet<string>(StringComparer.Ordinal);
        var claimedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (input, day, error) in parsed)
        {
            if (error is not null) { planned.Add(Invalid(input, day, error)); continue; }
            var d = day!.Value;

            // Resolve project (id preferred; else name).
            long? projectId = input.ProjectId;
            string? projectName = null;
            if (projectId is not null)
            {
                if (!byId.TryGetValue(projectId.Value, out var project)) { planned.Add(Invalid(input, d, $"project id {projectId} not found in workspace")); continue; }
                projectName = project.Name;
            }
            else if (!string.IsNullOrWhiteSpace(input.ProjectName))
            {
                var match = projects.FirstOrDefault(project => project.Name.Equals(input.ProjectName, StringComparison.OrdinalIgnoreCase))
                            ?? projects.FirstOrDefault(project => project.Name.Contains(input.ProjectName!, StringComparison.OrdinalIgnoreCase));
                if (match is null) { planned.Add(Invalid(input, d, $"project '{input.ProjectName}' not found")); continue; }
                projectId = match.Id;
                projectName = match.Name;
            }

            var baseEntry = new PlannedEntry
            {
                Day = d,
                SourceUid = input.SourceUid,
                // Verbatim: what the user approves in the preview is byte-for-byte what Toggl gets.
                // Identity lives in the ledger and in the entry's own content, never in this string.
                Description = input.Description,
                Start = input.Start,
                DurationSeconds = input.DurationSeconds,
                ProjectId = projectId,
                ProjectName = projectName,
                TaskId = input.TaskId,
                Tags = input.Tags,
                Status = PlannedEntry.StatusNew,
            };

            if (!locks.IsEditable(d))
            {
                var dayLock = locks.GetWeekLock(d);
                planned.Add(baseEntry with { Status = PlannedEntry.StatusLocked, Note = Invariant($"week is locked (closed {dayLock.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it") });
                continue;
            }

            var dup = duplicates.Find(input.SourceUid, input.Start, input.DurationSeconds, projectId);
            if (dup is not null)
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = dup.Reason, ExistingEntryId = dup.EntryId });
                continue;
            }

            // Another proposed entry in this same batch already claims this sourceUid or this slot.
            var fingerprint = EntryFingerprint.TryFor(input.Start, input.DurationSeconds, projectId);
            if (!claimedUids.Add(input.SourceUid))
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = "duplicate of another proposed entry with the same sourceUid" });
                continue;
            }
            if (fingerprint is not null && !claimedFingerprints.Add(fingerprint))
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = "duplicate of another proposed entry with the same start, length and project" });
                continue;
            }

            planned.Add(baseEntry);
        }

        var newEntries = planned.Where(p => p.Status == PlannedEntry.StatusNew).ToList();
        var newCount = newEntries.Count;
        var dupCount = planned.Count(p => p.Status == PlannedEntry.StatusDuplicate);
        var blocked = planned.Count(p => p.Status is PlannedEntry.StatusLocked or PlannedEntry.StatusInvalid);

        // Report the week (and its lock deadline) of the committable entries, not the earliest
        // submitted day — otherwise a stray locked past-week entry would make the summary claim
        // WeekLocked=true even though there's something to commit.
        var reportDay = newCount > 0 ? newEntries.Min(p => p.Day) : minDay;
        var weekStart = WeekLockPolicy.WeekStartOf(reportDay);
        var lockInfo = locks.GetWeekLock(weekStart);

        // Freeze the plan only if there's something to write; the whole classified batch is stored
        // (not just the new ones) so commit_plan sees the full context under one planId.
        string? planId = null;
        if (newCount > 0)
        {
            planId = Guid.NewGuid().ToString("N");
            plans.Save(new WeekPlan
            {
                Id = planId,
                UserKey = tokens.UserKey,
                WeekStart = weekStart,
                CreatedAt = clock.GetUtcNow(),
                Entries = planned,
            });
        }

        var summary = planId is null
            ? $"Nothing to write ({dupCount} already logged, {blocked} blocked). No plan created."
            : $"{newCount} entr{(newCount == 1 ? "y" : "ies")} ready — review, then commit_plan(\"{planId}\"). {dupCount} duplicate, {blocked} blocked.";

        return new PlanResult(planId, weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lockInfo.IsLocked, locks.DeadlineOf(lockInfo),
            newCount, dupCount, blocked, planned.Select(ToDto).ToList(), summary);
    }

    // True if the start string carries an explicit offset/Z — DateTimeOffset.TryParse would
    // otherwise silently assume local time, mis-dating the entry near midnight.
    private static bool HasExplicitOffset(string s) => OffsetSuffix.IsMatch(s.Trim());

    // Builds a rejected PlannedEntry that echoes the input for display; Day falls back to
    // DateOnly.MinValue when the start couldn't even be parsed.
    private static PlannedEntry Invalid(PlanEntryInput input, DateOnly? day, string reason) => new()
    {
        Day = day ?? DateOnly.MinValue,
        SourceUid = input.SourceUid,
        Description = input.Description,
        Start = input.Start,
        DurationSeconds = input.DurationSeconds,
        ProjectId = input.ProjectId,
        ProjectName = input.ProjectName,
        TaskId = input.TaskId,
        Tags = input.Tags,
        Status = PlannedEntry.StatusInvalid,
        Note = reason,
    };

    // Projects an internal PlannedEntry to its client-facing DTO (DateOnly -> yyyy-MM-dd,
    // null Tags -> empty list).
    private static PlanEntryDto ToDto(PlannedEntry e) => new(
        e.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.SourceUid, e.Description, e.Start, e.DurationSeconds,
        e.ProjectId, e.ProjectName, e.TaskId, e.Tags ?? [], e.Status, e.Note);
}
