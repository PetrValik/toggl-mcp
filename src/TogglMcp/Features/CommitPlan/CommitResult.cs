namespace TogglMcp.Features.CommitPlan;

/// <summary>Per-entry outcome of a commit: how one planned entry fared against Toggl.</summary>
/// <param name="SourceUid">Stable id of the planned entry (ties back to the plan and its dedup marker).</param>
/// <param name="Status">One of "created" | "duplicate" | "skipped" | "error".</param>
/// <param name="Message">Human/model-facing detail (e.g. the created entry id, or why it was skipped).</param>
/// <param name="EntryId">Toggl time-entry id when known (on create, or the pre-existing duplicate); null otherwise.</param>
public sealed record CommitEntryResult(string SourceUid, string Status, string Message, long? EntryId);

/// <summary>Aggregate result of a commit_plan call — the counts plus the per-entry breakdown returned to the caller.</summary>
/// <param name="PlanId">The plan that was committed (echoes the request).</param>
/// <param name="Created">Number of entries newly written to Toggl.</param>
/// <param name="Skipped">Entries not written: duplicates or lock-blocked days.</param>
/// <param name="Failed">Entries whose Toggl write errored.</param>
/// <param name="Results">Per-entry outcomes, one <see cref="CommitEntryResult"/> per attempted entry.</param>
/// <param name="Summary">One-line human-readable roll-up of the counts.</param>
public sealed record CommitResult(
    string PlanId,
    int Created,
    int Skipped,
    int Failed,
    IReadOnlyList<CommitEntryResult> Results,
    string Summary);
