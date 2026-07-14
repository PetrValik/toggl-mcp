namespace TogglMcp.Planning;

/// <summary>
/// The dedup marker embedded in a time entry's description (`⟨sourceUid⟩`). Module-shared
/// because two slices depend on it — PlanWeek writes/dedups against it and CommitPlan
/// re-checks it. Kept here rather than duplicated so the two slices don't couple sideways.
/// </summary>
public static class SourceMarker
{
    /// <summary>Wraps a source uid in the ⟨…⟩ marker exactly as it is embedded in a description, so writers and dedup scanners agree on one format.</summary>
    public static string For(string sourceUid) => $"⟨{sourceUid}⟩";
}
