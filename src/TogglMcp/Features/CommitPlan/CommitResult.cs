namespace TogglMcp.Features.CommitPlan;

public sealed record CommitEntryResult(string SourceUid, string Status, string Message, long? EntryId);

public sealed record CommitResult(
    string PlanId,
    int Created,
    int Skipped,
    int Failed,
    IReadOnlyList<CommitEntryResult> Results,
    string Summary);
