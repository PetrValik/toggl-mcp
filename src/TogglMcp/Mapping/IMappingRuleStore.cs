namespace TogglMcp.Mapping;

/// <summary>Persists per-user mapping rules so <c>suggest_project</c> gets quieter over time.</summary>
public interface IMappingRuleStore
{
    /// <summary>Returns every learned rule for the given user; empty if none have been recorded yet.</summary>
    Task<IReadOnlyList<MappingRule>> GetRulesAsync(string userKey, CancellationToken ct = default);

    /// <summary>Adds a rule (replacing any existing rule for the same keyword, case-insensitive).</summary>
    Task AddRuleAsync(string userKey, MappingRule rule, CancellationToken ct = default);
}
