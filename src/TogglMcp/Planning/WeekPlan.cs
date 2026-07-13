namespace TogglMcp.Planning;

/// <summary>
/// One resolved entry inside a frozen plan. <see cref="Description"/> already
/// includes the <c>⟨sourceUid⟩</c> dedup marker, so commit is a verbatim replay.
/// </summary>
public sealed record PlannedEntry
{
    public required DateOnly Day { get; init; }
    public required string SourceUid { get; init; }
    /// <summary>Final description as it will be written (marker included).</summary>
    public required string Description { get; init; }
    /// <summary>RFC3339 with the source's own offset.</summary>
    public required string Start { get; init; }
    public required long DurationSeconds { get; init; }
    public long? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public long? TaskId { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary><c>new</c> (will be written), <c>duplicate</c>, <c>locked</c>, or <c>invalid</c>.</summary>
    public required string Status { get; init; }
    /// <summary>Reason for a non-<c>new</c> status.</summary>
    public string? Note { get; init; }
    /// <summary>For <c>duplicate</c>: the existing Toggl entry id.</summary>
    public long? ExistingEntryId { get; init; }

    public const string StatusNew = "new";
    public const string StatusDuplicate = "duplicate";
    public const string StatusLocked = "locked";
    public const string StatusInvalid = "invalid";
}

/// <summary>A frozen proposal for a week, produced by plan_week and replayed by commit_plan.</summary>
public sealed record WeekPlan
{
    public required string Id { get; init; }
    public required string UserKey { get; init; }
    public required DateOnly WeekStart { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<PlannedEntry> Entries { get; init; }
}
