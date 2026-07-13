namespace TogglMcp.Planning;

/// <summary>Holds frozen week plans between plan_week and commit_plan.</summary>
public interface IPlanStore
{
    void Save(WeekPlan plan);

    /// <summary>Returns the plan if it exists, belongs to <paramref name="userKey"/>, and hasn't expired.</summary>
    WeekPlan? Get(string userKey, string planId);
}
