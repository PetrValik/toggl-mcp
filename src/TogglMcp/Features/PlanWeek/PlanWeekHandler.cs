using System.Globalization;
using System.Text.RegularExpressions;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.PlanWeek;

internal sealed class PlanWeekHandler(
    ITogglClient toggl,
    ITogglTokenProvider tokens,
    WeekLockPolicy locks,
    IPlanStore plans,
    TimeProvider clock)
{
    // RFC3339 explicit offset: trailing 'Z' or ±HH[:]MM / ±HH.
    private static readonly Regex OffsetSuffix = new(@"(?:[Zz]|[+-]\d{2}(?::?\d{2})?)$", RegexOptions.Compiled);

    public async Task<PlanResult> HandleAsync(PlanEntryInput[] entries, CancellationToken cancellationToken)
    {
        if (entries is null || entries.Length == 0)
            return new PlanResult(null, "", false, null, 0, 0, 0, [], "No entries proposed.");

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
            else day = locks.LocalDayOf(dto);
            return (In: entry, Day: day, Error: error);
        }).ToList();

        // If nothing is even shape-valid, return the invalids without touching Toggl.
        if (parsed.All(p => p.Error is not null))
        {
            var invalids = parsed.Select(p => ToDto(Invalid(p.In, p.Day, p.Error!))).ToList();
            return new PlanResult(null, "", false, null, 0, 0, invalids.Count, invalids,
                $"All {invalids.Count} proposed entr{(invalids.Count == 1 ? "y is" : "ies are")} invalid; nothing to plan.");
        }

        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var byId = projects.ToDictionary(project => project.Id);

        var validDays = parsed.Where(p => p.Day is not null).Select(p => p.Day!.Value).ToList();
        var minDay = validDays.Count > 0 ? validDays.Min() : locks.Today();
        var maxDay = validDays.Count > 0 ? validDays.Max() : locks.Today();
        var existing = await toggl.GetTimeEntriesAsync(minDay, maxDay.AddDays(1), cancellationToken);

        var planned = new List<PlannedEntry>();
        var plannedMarkers = new HashSet<string>(StringComparer.Ordinal); // dedup within this batch
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

            var marker = SourceMarker.For(input.SourceUid);
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
                var dayLock = locks.GetWeekLock(d);
                planned.Add(baseEntry with { Status = PlannedEntry.StatusLocked, Note = $"week is locked (closed {dayLock.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it" });
                continue;
            }

            var dup = existing.FirstOrDefault(x => x.Description?.Contains(marker, StringComparison.Ordinal) == true);
            if (dup is not null)
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = $"already logged (entry {dup.Id})", ExistingEntryId = dup.Id });
                continue;
            }

            // Another proposed entry in this same batch already claims this sourceUid.
            if (!plannedMarkers.Add(marker))
            {
                planned.Add(baseEntry with { Status = PlannedEntry.StatusDuplicate, Note = "duplicate of another proposed entry with the same sourceUid" });
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

        return new PlanResult(planId, weekStart.ToString("yyyy-MM-dd"), lockInfo.IsLocked, Fmt(lockInfo.LocksAt),
            newCount, dupCount, blocked, planned.Select(ToDto).ToList(), summary);
    }

    private static bool HasExplicitOffset(string s) => OffsetSuffix.IsMatch(s.Trim());

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
}
