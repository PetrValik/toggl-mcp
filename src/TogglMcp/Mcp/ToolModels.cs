using System.ComponentModel;

namespace TogglMcp.Mcp;

// Stable, purpose-built shapes returned to the model (decoupled from Toggl wire models).

public sealed record ProjectDto(long Id, string Name, bool Active, long? ClientId);

public sealed record TaskDto(long Id, string Name, long ProjectId, bool Active);

public sealed record EntryDto(
    long Id,
    string? Description,
    string? Start,
    long DurationSeconds,
    long? ProjectId,
    string? ProjectName,
    long? TaskId,
    IReadOnlyList<string> Tags);

// ── week_status ─────────────────────────────────────────────────────────────
public sealed record DayStatus(
    string Date,
    string DayOfWeek,
    bool Working,
    double HoursLogged,
    bool Empty,
    bool Editable,
    string? LocksAt);

public sealed record WeekStatusResult(
    string WeekStart,
    string WeekEnd,
    bool Locked,
    string? LocksAt,
    IReadOnlyList<DayStatus> Days,
    string Summary);

// ── plan_week ───────────────────────────────────────────────────────────────

/// <summary>An entry the model proposes for the week. Nothing is written by plan_week.</summary>
public sealed record PlanEntryInput
{
    [Description("What was worked on (free text).")]
    public string Description { get; init; } = "";
    [Description("Start time RFC3339 WITH offset, e.g. 2026-07-13T09:00:00+02:00. Its date decides which day/week the entry counts for.")]
    public string Start { get; init; } = "";
    [Description("Duration in seconds, positive.")]
    public long DurationSeconds { get; init; }
    [Description("REQUIRED stable id of the source record for idempotency, e.g. 'calevt:AB12' or 'gitsha:9f3c'.")]
    public string SourceUid { get; init; } = "";
    [Description("Toggl project id (preferred, from list_projects). Omit for 'No Project'.")]
    public long? ProjectId { get; init; }
    [Description("Project name to resolve to an id if the id isn't known.")]
    public string? ProjectName { get; init; }
    [Description("Toggl task id. Requires a project.")]
    public long? TaskId { get; init; }
    [Description("Optional tag names.")]
    public string[]? Tags { get; init; }
}

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

// ── commit_plan ─────────────────────────────────────────────────────────────
public sealed record CommitEntryResult(string SourceUid, string Status, string Message, long? EntryId);

public sealed record CommitResult(
    string PlanId,
    int Created,
    int Skipped,
    int Failed,
    IReadOnlyList<CommitEntryResult> Results,
    string Summary);

/// <summary>
/// Result of <c>suggest_project</c>. <c>Matched=true</c> means a user rule fired (act
/// silently); <c>false</c> means this is a best-effort guess — confirm with the user
/// before logging, then teach it with <c>add_mapping_rule</c>.
/// </summary>
public sealed record SuggestProjectResult(
    bool Matched,
    long? ProjectId,
    string? ProjectName,
    double Confidence,
    string Reason,
    IReadOnlyList<ProjectDto> Candidates);

public sealed record AddMappingRuleResult(bool Ok, string Message, string Keyword, long ProjectId, string? ProjectName);
