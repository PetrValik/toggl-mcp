namespace TogglMcp.Planning;

/// <summary>Holds frozen week plans between plan_week and commit_plan.</summary>
public interface IPlanStore
{
    /// <summary>Freezes a plan under its own id so a later commit_plan can replay it verbatim.</summary>
    void Save(WeekPlan plan);

    /// <summary>Returns the plan if it exists, belongs to <paramref name="userKey"/>, and hasn't expired.</summary>
    // Ownership + expiry are enforced here so a stale or foreign plan id reads as absent.
    WeekPlan? Get(string userKey, string planId);
}
