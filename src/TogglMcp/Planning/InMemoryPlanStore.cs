using System.Collections.Concurrent;

namespace TogglMcp.Planning;

/// <summary>
/// In-process plan store with a short TTL. Plans are ephemeral review artifacts —
/// losing them on restart just means re-running plan_week, so memory is fine.
/// Keyed by plan id; ownership is checked against the requesting user on read.
/// </summary>
public sealed class InMemoryPlanStore : IPlanStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, WeekPlan> _plans = new();
    private readonly TimeProvider _clock;

    public InMemoryPlanStore(TimeProvider clock) => _clock = clock;

    public void Save(WeekPlan plan)
    {
        Prune();
        _plans[plan.Id] = plan;
    }

    public WeekPlan? Get(string userKey, string planId)
    {
        Prune();
        if (_plans.TryGetValue(planId, out var plan) &&
            plan.UserKey == userKey &&
            !IsExpired(plan))
            return plan;
        return null;
    }

    private bool IsExpired(WeekPlan plan) => _clock.GetUtcNow() - plan.CreatedAt > Ttl;

    private void Prune()
    {
        foreach (var (id, plan) in _plans)
            if (IsExpired(plan))
                _plans.TryRemove(id, out _);
    }
}
