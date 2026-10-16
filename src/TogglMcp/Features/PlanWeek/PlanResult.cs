namespace TogglMcp.Features.PlanWeek;

/// <summary>
/// Flat, client-facing view of one planned entry in the preview — the wire shape shown to the
/// user (DateOnly rendered as yyyy-MM-dd, resolved project name filled in, Status/Note carrying
/// the per-entry verdict). Projected from the internal <c>PlannedEntry</c> via <c>ToDto</c>.
/// </summary>
public sealed record PlanEntryDto(
    string Day,                     // local calendar day the entry counts for (yyyy-MM-dd)
    string SourceUid,               // idempotency key echoed back
    string Description,             // description exactly as proposed — this is what Toggl gets
    string Start,                   // original RFC3339 start string, verbatim
    long DurationSeconds,
    long? ProjectId,                // resolved id (null = No Project or unresolved invalid)
    string? ProjectName,            // resolved name if a project matched
    long? TaskId,
    IReadOnlyList<string> Tags,     // never null — empty list when no tags
    string Status,                  // new | duplicate | locked | invalid (PlannedEntry.Status*)
    string? Note);                  // reason for duplicate/locked/invalid; null for new

/// <summary>Preview returned by plan_week. Commit the frozen plan with <c>commit_plan(PlanId)</c>.</summary>
public sealed record PlanResult(
    string? PlanId,                         // frozen-plan token to pass to commit_plan; null when nothing is committable
    string WeekStart,                       // Monday of the reported (committable) week, yyyy-MM-dd
    bool WeekLocked,                        // lock state of that reported week
    string? LocksAt,                        // when that week locks (formatted deadline); null when locking is off
    int NewCount,                           // entries ready to write
    int DuplicateCount,                     // skipped as already logged / duplicated within the batch
    int BlockedCount,                       // locked + invalid entries (nothing written for these)
    IReadOnlyList<PlanEntryDto> Entries,    // every proposed entry with its per-entry verdict
    string Summary);                        // one-line human/model-facing recap
