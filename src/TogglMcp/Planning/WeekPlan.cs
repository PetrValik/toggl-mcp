namespace TogglMcp.Planning;

/// <summary>
/// One resolved entry inside a frozen plan. <see cref="Description"/> is exactly what the user
/// approved in the preview, so commit is a verbatim replay of it.
/// </summary>
public sealed record PlannedEntry
{
    // Calendar day this entry lands on (source-local date).
    public required DateOnly Day { get; init; }
    // Stable id of the originating source item; the dedup key recorded in the ledger on commit.
    public required string SourceUid { get; init; }
    /// <summary>Final description as it will be written — the user's words, nothing appended.</summary>
    public required string Description { get; init; }
    /// <summary>RFC3339 with the source's own offset.</summary>
    public required string Start { get; init; }
    // Entry length in seconds (Toggl's native duration unit).
    public required long DurationSeconds { get; init; }
    // Resolved Toggl project id; null when mapping found no project.
    public long? ProjectId { get; init; }
    // Human-readable project name, carried for display in the plan preview.
    public string? ProjectName { get; init; }
    // Optional Toggl task id under the project.
    public long? TaskId { get; init; }
    // Tags to attach; null when none.
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary><c>new</c> (will be written), <c>duplicate</c>, <c>locked</c>, or <c>invalid</c>.</summary>
    public required string Status { get; init; }
    /// <summary>Reason for a non-<c>new</c> status.</summary>
    public string? Note { get; init; }
    /// <summary>For <c>duplicate</c>: the existing Toggl entry id.</summary>
    public long? ExistingEntryId { get; init; }

    // Canonical Status string constants — shared so plan_week and commit_plan classify identically.
    public const string StatusNew = "new";
    public const string StatusDuplicate = "duplicate";
    public const string StatusLocked = "locked";
    public const string StatusInvalid = "invalid";
}

/// <summary>A frozen proposal for a week, produced by plan_week and replayed by commit_plan.</summary>
public sealed record WeekPlan
{
    // Store key; also the token the caller passes back to commit_plan.
    public required string Id { get; init; }
    // Owner identity; commit is rejected if the requester's key doesn't match.
    public required string UserKey { get; init; }
    // Monday of the target week (source-local date).
    public required DateOnly WeekStart { get; init; }
    // Freeze timestamp; TTL/expiry in the plan store is measured from here.
    public required DateTimeOffset CreatedAt { get; init; }
    // The resolved entries in commit order.
    public required IReadOnlyList<PlannedEntry> Entries { get; init; }
}
