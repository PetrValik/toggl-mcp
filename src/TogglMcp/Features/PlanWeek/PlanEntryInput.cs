using System.ComponentModel;

namespace TogglMcp.Features.PlanWeek;

/// <summary>An entry the model proposes for the week. Nothing is written by plan_week.</summary>
public sealed record PlanEntryInput
{
    [Description("What was worked on (free text).")]
    public string Description { get; init; } = "";
    [Description("Start time RFC3339 WITH offset, e.g. 2026-07-13T09:00:00+02:00. Its date decides which day/week the entry counts for.")]
    public string Start { get; init; } = "";
    // Rejected unless > 0 (see PlanWeekHandler shape check).
    [Description("Duration in seconds, positive.")]
    public long DurationSeconds { get; init; }
    // The idempotency key: becomes the ⟨sourceUid⟩ dedup marker in the description so the
    // same work is never logged twice across plan/commit runs. Required.
    [Description("REQUIRED stable id of the source record for idempotency, e.g. 'calevt:AB12' or 'gitsha:9f3c'.")]
    public string SourceUid { get; init; } = "";
    // null => resolve via ProjectName; both null => 'No Project'.
    [Description("Toggl project id (preferred, from list_projects). Omit for 'No Project'.")]
    public long? ProjectId { get; init; }
    // Fallback when ProjectId is unknown: exact (case-insensitive) match preferred, else substring.
    [Description("Project name to resolve to an id if the id isn't known.")]
    public string? ProjectName { get; init; }
    // Requires a project (id or name); a taskId without one is a shape error.
    [Description("Toggl task id. Requires a project.")]
    public long? TaskId { get; init; }
    // null and empty are equivalent — no tags.
    [Description("Optional tag names.")]
    public string[]? Tags { get; init; }
}
