using TogglMcp.Configuration;
using TogglMcp.Lock;
using TogglMcp.Tests.Support;

namespace TogglMcp.Tests.Lock;

/// <summary>
/// What someone gets who installs the server and configures nothing about locking. Most Toggl
/// workspaces never lock weeks, so out of the box no week is refused and days follow the
/// machine's own clock rather than one particular team's time zone.
/// </summary>
public sealed class WeekLockPolicyDefaultsTests
{
    [Fact]
    public void A_default_install_never_refuses_a_week()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var policy = new WeekLockPolicy(new LockOptions(), clock);

        var monthsAgo = new DateOnly(2026, 6, 1);

        Assert.False(policy.GetWeekLock(monthsAgo).IsLocked);
        Assert.True(policy.IsEditable(monthsAgo));
    }

    [Fact]
    public void No_team_time_zone_is_pinned_by_default() =>
        Assert.Equal("", new LockOptions().TimeZone);

    [Fact]
    public void A_blank_time_zone_means_the_machine_clock()
    {
        // Late evening UTC: already the next day anywhere east of UTC, so a UTC fallback
        // would put "today" on the wrong date for those machines.
        var now = new DateTimeOffset(2026, 10, 16, 23, 30, 0, TimeSpan.Zero);
        var policy = new WeekLockPolicy(new LockOptions { TimeZone = "" }, new MutableTimeProvider(now));

        var machineToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);

        Assert.Equal(machineToday, policy.Today());
    }

    [Fact]
    public void A_configured_time_zone_still_wins_over_the_machine_clock()
    {
        var now = new DateTimeOffset(2026, 10, 16, 23, 30, 0, TimeSpan.Zero);
        var policy = new WeekLockPolicy(new LockOptions { TimeZone = "Pacific/Kiritimati" }, new MutableTimeProvider(now));

        // UTC+14: well into the 17th.
        Assert.Equal(new DateOnly(2026, 10, 17), policy.Today());
    }
}
