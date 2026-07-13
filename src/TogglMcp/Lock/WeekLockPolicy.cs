using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Lock;

/// <summary>Lock state of a single (Mon–Sun) week.</summary>
public sealed record WeekLockInfo(DateOnly WeekStart, DateOnly WeekEnd, DateTimeOffset LocksAt, bool IsLocked)
{
    public bool Editable => !IsLocked;
}

/// <summary>
/// Computes the weekly timesheet lock (see <see cref="LockOptions"/>). Weeks run
/// Monday–Sunday; a week locks on the configured close day/time of the week
/// <c>GraceWeeks</c> later, in the configured time zone. Deterministic and
/// clock-injectable so it can be tested without waiting for a real Friday.
/// </summary>
public sealed class WeekLockPolicy
{
    private readonly LockOptions _o;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _tz;

    public WeekLockPolicy(IOptions<LockOptions> options, TimeProvider clock)
        : this(options.Value, clock) { }

    public WeekLockPolicy(LockOptions options, TimeProvider clock)
    {
        _o = options;
        _clock = clock;
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(_o.TimeZone); }
        catch (TimeZoneNotFoundException) { _tz = TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { _tz = TimeZoneInfo.Utc; }
    }

    /// <summary>Monday of the week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekStartOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Today's date in the lock time zone.</summary>
    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _tz).DateTime);

    /// <summary>Calendar day of an instant in the lock time zone (which day it "counts as").</summary>
    public DateOnly LocalDayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _tz).DateTime);

    public DateOnly CurrentWeekStart() => WeekStartOf(Today());

    public WeekLockInfo GetWeekLock(DateOnly anyDayInWeek)
    {
        var start = WeekStartOf(anyDayInWeek);
        var end = start.AddDays(6);

        // Locks on CloseDayOfWeek of the week GraceWeeks later, at CloseTime local.
        var lockWeekStart = start.AddDays(7 * _o.GraceWeeks);
        var closeDayOffset = ((int)_o.CloseDayOfWeek + 6) % 7; // Monday=0 … Sunday=6
        var lockDate = lockWeekStart.AddDays(closeDayOffset);

        var localLock = lockDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(_o.CloseTime);
        var locksAt = new DateTimeOffset(localLock, _tz.GetUtcOffset(localLock));

        var isLocked = _o.Enabled && _clock.GetUtcNow() >= locksAt;
        return new WeekLockInfo(start, end, locksAt, isLocked);
    }

    /// <summary>True if entries dated <paramref name="d"/> can still be written.</summary>
    public bool IsEditable(DateOnly d) => !_o.Enabled || !GetWeekLock(d).IsLocked;

    public string AdminContact => _o.AdminContact;
    public bool Enabled => _o.Enabled;
}
