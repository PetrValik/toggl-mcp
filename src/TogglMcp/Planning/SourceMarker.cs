namespace TogglMcp.Planning;

/// <summary>
/// The dedup marker embedded in a time entry's description (`⟨sourceUid⟩`). Module-shared
/// because two slices depend on it — PlanWeek writes/dedups against it and CommitPlan
/// re-checks it. Kept here rather than duplicated so the two slices don't couple sideways.
/// </summary>
public static class SourceMarker
{
    public static string For(string sourceUid) => $"⟨{sourceUid}⟩";
}
