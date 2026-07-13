using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Mcp;

/// <summary>
/// The weekly back-fill flow: <c>read → propose → approve → write</c>, enforced.
/// The model prepares a proposal with <c>plan_week</c> (nothing is written); the user
/// reviews it; <c>commit_plan</c> then writes exactly the frozen plan. There is no
/// direct-write tool, so a write can only ever be a replay of an approved plan.
///
/// Lock-aware throughout: weeks close permanently (Fri 23:59, per <see cref="WeekLockPolicy"/>)
/// and locked days are refused, never attempted.
/// </summary>
[McpServerToolType]
public static class PlanningTools
{
    [McpServerTool(Name = "week_status", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description(
        "Show, for a week (default the current week), which working days are empty vs already filled in Toggl, plus the lock deadline (when the week closes permanently). Use this first to see what still needs back-filling before proposing entries with plan_week.")]
    public static async Task<WeekStatusResult> WeekStatus(
        ITogglClient toggl,
        WeekLockPolicy locks,
        [Description("Any date in the target week (YYYY-MM-DD). Defaults to the current week.")] string? weekOf = null,
        CancellationToken ct = default)
    {
        var anchor = ParseDate(weekOf) ?? locks.Today();
        var weekStart = WeekLockPolicy.WeekStartOf(anchor);
        var weekEnd = weekStart.AddDays(6);
        var lockInfo = locks.GetWeekLock(weekStart);

        var entries = await toggl.GetTimeEntriesAsync(weekStart, weekStart.AddDays(7), ct);
        var perDay = new Dictionary<DateOnly, double>();
        foreach (var e in entries)
        {
            if (e.Duration <= 0) continue; // ignore running/negative entries
            if (!DateTimeOffset.TryParse(e.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) continue;
            var d = locks.LocalDayOf(dto);
            perDay[d] = perDay.GetValueOrDefault(d) + e.Duration / 3600.0;
        }

        var days = new List<DayStatus>();
        for (var i = 0; i < 7; i++)
        {
            var d = weekStart.AddDays(i);
            var working = d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var hours = Math.Round(perDay.GetValueOrDefault(d), 2);
            var wl = locks.GetWeekLock(d);
            days.Add(new DayStatus(d.ToString("yyyy-MM-dd"), d.DayOfWeek.ToString(), working, hours, hours <= 0, wl.Editable, Fmt(wl.LocksAt)));
        }

        var emptyWorking = days.Where(x => x.Working && x.Empty).Select(x => x.DayOfWeek).ToList();
        var range = $"{weekStart:MMM d}–{weekEnd:MMM d}";
        var summary = lockInfo.IsLocked
            ? $"Week {range} is LOCKED (closed {lockInfo.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it."
            : emptyWorking.Count == 0
                ? $"Week {range}: all working days filled. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}."
                : $"Week {range}: {emptyWorking.Count} empty working day(s): {string.Join(", ", emptyWorking)}. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}.";

        return new WeekStatusResult(weekStart.ToString("yyyy-MM-dd"), weekEnd.ToString("yyyy-MM-dd"),
            lockInfo.IsLocked, Fmt(lockInfo.LocksAt), days, summary);
    }

    [McpServerTool(Name = "plan_week", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description(
        "Prepare (but DO NOT write) a back-fill proposal for a week. Pass one entry per piece of work for the empty working days you want to fill — simply omit weekends and any days off (sick/vacation). Each entry is validated, its project resolved, deduped against what's already in Toggl, and checked against the lock. Returns a preview with a planId and per-entry status (new/duplicate/locked/invalid). Show it to the user; on approval call commit_plan(planId) to actually write it.")]
    public static async Task<PlanResult> PlanWeek(
        ITogglClient toggl,
        ITogglTokenProvider tokens,
        WeekLockPolicy locks,
        IPlanStore plans,
        TimeProvider clock,
        [Description("Proposed entries for the week's empty working days.")] PlanEntryInput[] entries,
        CancellationToken ct = default)
    {
        if (entries is null || entries.Length == 0)
            return new PlanResult(null, "", false, null, 0, 0, 0, [], "No entries proposed.");

        // First pass: parse + validate shape — no Toggl calls yet.
        var parsed = entries.Select(e =>
        {
            string? err = null;
            DateOnly? day = null;
            if (string.IsNullOrWhiteSpace(e.SourceUid)) err = "sourceUid is required.";
            else if (e.DurationSeconds <= 0) err = "durationSeconds must be positive.";
            else if (e.TaskId is not null && e.ProjectId is null && string.IsNullOrWhiteSpace(e.ProjectName)) err = "taskId requires a project.";
            else if (!DateTimeOffset.TryParse(e.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) || !HasExplicitOffset(e.Start))
                err = $"start '{e.Start}' must be RFC3339 with an explicit offset (e.g. 2026-07-13T09:00:00+02:00).";
            else day = locks.LocalDayOf(dto);
            return (In: e, Day: day, Error: err);
        }).ToList();

        // If nothing is even shape-valid, return the invalids without touching Toggl.
        if (parsed.All(p => p.Error is not null))
        {
            var invalids = parsed.Select(p => ToDto(Invalid(p.In, p.Day, p.Error!))).ToList();
            return new PlanResult(null, "", false, null, 0, 0, invalids.Count, invalids,
                $"All {invalids.Count} proposed entr{(invalids.Count == 1 ? "y is" : "ies are")} invalid; nothing to plan.");
        }

        var projects = await toggl.GetProjectsAsync(ct);
        var byId = projects.ToDictionary(p => p.Id);

        var validDays = parsed.Where(p => p.Day is not null).Select(p => p.Day!.Value).ToList();
        var minDay = validDays.Count > 0 ? validDays.Min() : locks.Today();
        var maxDay = validDays.Count > 0 ? validDays.Max() : locks.Today();
        var existing = await toggl.GetTimeEntriesAsync(minDay, maxDay.AddDays(1), ct);

        var planned = new List<PlannedEntry>();
        foreach (var (input, day, error) in parsed)
        {
            if (error is not null) { planned.Add(Invalid(input, day, error)); continue; }
            var d = day!.Value;

            // Resolve project (id preferred; else name).
            long? projectId = input.ProjectId;
            string? projectName = null;
            if (projectId is not null)
            {
                if (!byId.TryGetValue(projectId.Value, out var p)) { planned.Add(Invalid(input, d, $"project id {projectId} not found in workspace")); continue; }
                projectName = p.Name;
            }
            else if (!string.IsNullOrWhiteSpace(input.ProjectName))
            {
                var match = projects.FirstOrDefault(p => p.Name.Equals(input.ProjectName, StringComparison.OrdinalIgnoreCase))
                            ?? projects.FirstOrDefault(p => p.Name.Contains(input.ProjectName!, StringComparison.OrdinalIgnoreCase));
                if (match is null) { planned.Add(Invalid(input, d, $"project '{input.ProjectName}' not found")); continue; }
                projectId = match.Id;
                projectName = match.Name;
            }

            var marker = Marker(input.SourceUid);
            var description = string.IsNullOrWhiteSpace(input.Description) ? marker : $"{input.Description} {marker}";
            var baseEntry = new PlannedEntry
            {
                Day = d,
                SourceUid = input.SourceUid,
                Description = description,
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
                var wl = locks.GetWeekLock(d);
                planned.Add(baseEntry with { Status = PlannedEntry.StatusLocked, Note = $"week is locked (closed {wl.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it" });
                continue;
            }

            var dup = existing.FirstOrDefault(x => x.Description?.Contains(marker, StringComparison.Ordinal) == true);
            if (dup is not null)
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = $"already logged (entry {dup.Id})", ExistingEntryId = dup.Id });
                continue;
            }

            planned.Add(baseEntry);
        }

        var newCount = planned.Count(p => p.Status == PlannedEntry.StatusNew);
        var dupCount = planned.Count(p => p.Status == PlannedEntry.StatusDuplicate);
        var blocked = planned.Count(p => p.Status is PlannedEntry.StatusLocked or PlannedEntry.StatusInvalid);

        var weekStart = WeekLockPolicy.WeekStartOf(minDay);
        var lockInfo = locks.GetWeekLock(weekStart);

        string? planId = null;
        if (newCount > 0)
        {
            planId = Guid.NewGuid().ToString("N")[..10];
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

        return new PlanResult(planId, weekStart.ToString("yyyy-MM-dd"), lockInfo.IsLocked, Fmt(lockInfo.LocksAt),
            newCount, dupCount, blocked, planned.Select(ToDto).ToList(), summary);
    }

    [McpServerTool(Name = "commit_plan", Idempotent = true, OpenWorld = true),
     Description(
        "Write a previously prepared plan to Toggl — the ONLY write path. Pass the planId from plan_week. Writes exactly the frozen 'new' entries (a verbatim replay of what the user approved), re-checking the lock and dedup at write time. Optionally exclude some entries by sourceUid (days the user decided to skip).")]
    public static async Task<CommitResult> CommitPlan(
        ITogglClient toggl,
        ITogglTokenProvider tokens,
        WeekLockPolicy locks,
        IPlanStore plans,
        [Description("planId returned by plan_week.")] string planId,
        [Description("Optional sourceUids to skip (e.g. days the user decided not to log).")] string[]? exclude = null,
        CancellationToken ct = default)
    {
        var plan = plans.Get(tokens.UserKey, planId);
        if (plan is null)
            return new CommitResult(planId, 0, 0, 0, [], "Plan not found or expired — run plan_week again.");

        var excludeSet = new HashSet<string>(exclude ?? [], StringComparer.Ordinal);
        var wid = await toggl.GetWorkspaceIdAsync(ct);
        var toWrite = plan.Entries.Where(e => e.Status == PlannedEntry.StatusNew && !excludeSet.Contains(e.SourceUid)).ToList();

        var dayCache = new Dictionary<DateOnly, IReadOnlyList<TogglTimeEntry>>();
        var results = new List<CommitEntryResult>();
        int created = 0, skipped = 0, failed = 0;

        foreach (var e in toWrite)
        {
            if (!locks.IsEditable(e.Day))
            {
                results.Add(new CommitEntryResult(e.SourceUid, "skipped", $"week locked since planning; ask {locks.AdminContact} to reopen it", null));
                skipped++;
                continue;
            }

            // Defensive re-dedup (e.g. plan committed twice, or a race): one fetch per day, cached.
            if (!dayCache.TryGetValue(e.Day, out var dayEntries))
            {
                dayEntries = await toggl.GetTimeEntriesAsync(e.Day, e.Day.AddDays(1), ct);
                dayCache[e.Day] = dayEntries;
            }
            var marker = Marker(e.SourceUid);
            var dup = dayEntries.FirstOrDefault(x => x.Description?.Contains(marker, StringComparison.Ordinal) == true);
            if (dup is not null)
            {
                results.Add(new CommitEntryResult(e.SourceUid, "duplicate", $"already logged (entry {dup.Id})", dup.Id));
                skipped++;
                continue;
            }

            try
            {
                var entry = await toggl.CreateTimeEntryAsync(new CreateTimeEntryRequest
                {
                    CreatedWith = "toggl-mcp",
                    WorkspaceId = wid,
                    Start = e.Start,
                    Duration = e.DurationSeconds,
                    Description = e.Description,
                    ProjectId = e.ProjectId,
                    TaskId = e.TaskId,
                    Tags = e.Tags is { Count: > 0 } ? e.Tags : null,
                }, ct);
                results.Add(new CommitEntryResult(e.SourceUid, "created", $"entry {entry.Id} on {e.Day:yyyy-MM-dd}", entry.Id));
                created++;
            }
            catch (TogglException ex)
            {
                results.Add(new CommitEntryResult(e.SourceUid, "error", ex.Message, null));
                failed++;
            }
        }

        var excluded = plan.Entries.Count(e => e.Status == PlannedEntry.StatusNew && excludeSet.Contains(e.SourceUid));
        var summary = $"Committed {created}, skipped {skipped}, failed {failed}" + (excluded > 0 ? $"; {excluded} excluded by you." : ".");
        return new CommitResult(planId, created, skipped, failed, results, summary);
    }

    // Helpers ------------------------------------------------------------------

    private static readonly Regex OffsetSuffix = new(@"(?:[Zz]|[+-]\d{2}(?::?\d{2})?)$", RegexOptions.Compiled);
    private static bool HasExplicitOffset(string s) => OffsetSuffix.IsMatch(s.Trim());

    // Dedup marker embedded in the description; commit and dedup match on it.
    private static string Marker(string sourceUid) => $"⟨{sourceUid}⟩";

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

    private static PlanEntryDto ToDto(PlannedEntry e) => new(
        e.Day.ToString("yyyy-MM-dd"), e.SourceUid, e.Description, e.Start, e.DurationSeconds,
        e.ProjectId, e.ProjectName, e.TaskId, e.Tags ?? [], e.Status, e.Note);

    private static string Fmt(DateTimeOffset dto) => dto.ToString("yyyy-MM-dd HH:mm zzz");

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
