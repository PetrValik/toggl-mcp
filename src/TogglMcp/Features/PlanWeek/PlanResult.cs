namespace TogglMcp.Features.PlanWeek;

public sealed record PlanEntryDto(
    string Day,
    string SourceUid,
    string Description,
    string Start,
    long DurationSeconds,
    long? ProjectId,
    string? ProjectName,
    long? TaskId,
    IReadOnlyList<string> Tags,
    string Status,
    string? Note);

/// <summary>Preview returned by plan_week. Commit the frozen plan with <c>commit_plan(PlanId)</c>.</summary>
public sealed record PlanResult(
    string? PlanId,
    string WeekStart,
    bool WeekLocked,
    string? LocksAt,
    int NewCount,
    int DuplicateCount,
    int BlockedCount,
    IReadOnlyList<PlanEntryDto> Entries,
    string Summary);
