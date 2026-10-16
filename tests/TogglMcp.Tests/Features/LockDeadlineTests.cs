using TogglMcp.Configuration;
using TogglMcp.Features.PlanWeek;
using TogglMcp.Features.WeekStatus;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Tests.Support;

namespace TogglMcp.Tests.Features;

/// <summary>
/// A lock deadline is something the model repeats to the user ("you have until Friday"). With the
/// lock off there is no deadline, so none may be reported; with it on, the deadline is the point.
/// </summary>
public sealed class LockDeadlineTests : IDisposable
{
    private readonly TempDataDir _dir = new();
    private readonly FakeTogglClient _toggl = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero));

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private WeekLockPolicy Locks(bool enabled) =>
        new(new LockOptions { Enabled = enabled, TimeZone = "Europe/Prague" }, _clock);

    private PlanWeekHandler Planner(bool lockEnabled) =>
        new(_toggl, new FakeTokenProvider(Guid.NewGuid().ToString("N")), Locks(lockEnabled), new InMemoryPlanStore(_clock), new FileLedgerStore(_dir.Options, _clock), _clock);

    private static PlanEntryInput Proposal() =>
        new() { SourceUid = "calevt:AB12", Description = "Code review", Start = "2026-08-17T09:00:00+02:00", DurationSeconds = 3600, ProjectId = 123 };

    [Fact]
    public async Task With_the_lock_off_week_status_names_no_deadline()
    {
        var status = await new WeekStatusHandler(_toggl, Locks(enabled: false)).HandleAsync("2026-08-17", CancellationToken.None);

        Assert.Null(status.LocksAt);
        Assert.All(status.Days, d => Assert.Null(d.LocksAt));
        Assert.DoesNotContain("Lock", status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_the_lock_on_week_status_names_the_deadline()
    {
        var status = await new WeekStatusHandler(_toggl, Locks(enabled: true)).HandleAsync("2026-08-17", CancellationToken.None);

        Assert.NotNull(status.LocksAt);
        Assert.Contains("Locks 2026-08-28 23:59", status.Summary);
    }

    [Fact]
    public async Task With_the_lock_off_plan_week_names_no_deadline()
    {
        var plan = await Planner(lockEnabled: false).HandleAsync([Proposal()], CancellationToken.None);

        Assert.Null(plan.LocksAt);
    }

    [Fact]
    public async Task With_the_lock_on_plan_week_names_the_deadline()
    {
        var plan = await Planner(lockEnabled: true).HandleAsync([Proposal()], CancellationToken.None);

        Assert.NotNull(plan.LocksAt);
    }
}
