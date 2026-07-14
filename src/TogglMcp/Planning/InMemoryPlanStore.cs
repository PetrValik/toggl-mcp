using System.Collections.Concurrent;

namespace TogglMcp.Planning;

/// <summary>
/// In-process plan store with a short TTL. Plans are ephemeral review artifacts —
/// losing them on restart just means re-running plan_week, so memory is fine.
/// Keyed by plan id; ownership is checked against the requesting user on read.
/// </summary>
public sealed class InMemoryPlanStore : IPlanStore
{
    // Window a plan stays replayable — long enough to review then commit, short enough that stale plans die.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    // Thread-safe because tool calls can land concurrently on the shared server.
    private readonly ConcurrentDictionary<string, WeekPlan> _plans = new();
    // Injected clock so TTL/expiry is testable and honours the configured time source.
    private readonly TimeProvider _clock;

    public InMemoryPlanStore(TimeProvider clock) => _clock = clock;

    /// <summary>Stores (or overwrites) a plan keyed by its id, pruning expired ones first.</summary>
    public void Save(WeekPlan plan)
    {
        Prune();
        _plans[plan.Id] = plan;
    }

    /// <summary>Returns the plan only if present, owned by <paramref name="userKey"/>, and still within TTL.</summary>
    public WeekPlan? Get(string userKey, string planId)
    {
        Prune();
        // Ownership check prevents one user replaying another user's plan id.
        if (_plans.TryGetValue(planId, out var plan) &&
            plan.UserKey == userKey &&
            !IsExpired(plan))
            return plan;
        return null;
    }

    // True once the plan has outlived the TTL, measured from its creation time.
    private bool IsExpired(WeekPlan plan) => _clock.GetUtcNow() - plan.CreatedAt > Ttl;

    // Opportunistic sweep of expired plans on each access; keeps the dictionary from growing unbounded.
    private void Prune()
    {
        foreach (var (id, plan) in _plans)
            if (IsExpired(plan))
                _plans.TryRemove(id, out _);
    }
}
