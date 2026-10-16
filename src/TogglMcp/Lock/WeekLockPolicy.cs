using System.Globalization;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Lock;

/// <summary>Lock state of a single (Mon–Sun) week.</summary>
/// <param name="WeekStart">Monday of the week.</param>
/// <param name="WeekEnd">Sunday of the week (WeekStart + 6 days).</param>
/// <param name="LocksAt">Instant the week permanently locks (offset in the lock time zone).</param>
/// <param name="IsLocked">True once <see cref="LocksAt"/> has passed (and locking is enabled).</param>
public sealed record WeekLockInfo(DateOnly WeekStart, DateOnly WeekEnd, DateTimeOffset LocksAt, bool IsLocked)
{
    /// <summary>Convenience inverse of <see cref="IsLocked"/>: true while entries in this week may still be written.</summary>
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
    private readonly LockOptions _o;          // bound lock config (grace weeks, close day/time, zone, admin contact, enabled)
    private readonly TimeProvider _clock;     // injected clock; real in prod, fake in tests so lock deadlines are deterministic
    private readonly TimeZoneInfo _tz;        // resolved lock time zone; falls back to UTC if the configured id is invalid

    /// <summary>DI entry point: unwraps the bound <see cref="LockOptions"/> from the options monitor.</summary>
    public WeekLockPolicy(IOptions<LockOptions> options, TimeProvider clock)
        : this(options.Value, clock) { }

    /// <summary>Core ctor: resolves the configured time zone once (blank = the machine's own), degrading to UTC rather than throwing on a bad zone id (so a misconfig never crashes the server).</summary>
    public WeekLockPolicy(LockOptions options, TimeProvider clock)
    {
        _o = options;
        _clock = clock;
        if (string.IsNullOrWhiteSpace(_o.TimeZone)) { _tz = TimeZoneInfo.Local; return; }
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(_o.TimeZone); }
        catch (TimeZoneNotFoundException) { _tz = TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { _tz = TimeZoneInfo.Utc; }
    }

    /// <summary>Monday of the week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekStartOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Today's date in the lock time zone (not the host's local date — the zone is what defines week boundaries).</summary>
    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _tz).DateTime);

    /// <summary>Calendar day of an instant in the lock time zone (which day it "counts as").</summary>
    public DateOnly LocalDayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _tz).DateTime);

    /// <summary>Monday of the current week in the lock time zone.</summary>
    public DateOnly CurrentWeekStart() => WeekStartOf(Today());

    /// <summary>
    /// Resolves the full lock state for the week containing <paramref name="anyDayInWeek"/>:
    /// its Mon–Sun span and the exact instant it locks. The lock instant is built as a wall-clock
    /// time in the lock zone (so it survives DST correctly via the zone's offset at that instant),
    /// and a week is only ever "locked" when locking is enabled.
    /// </summary>
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

    /// <summary>Who a user should contact to amend an already-locked week; surfaced in tool error messages.</summary>
    public string AdminContact => _o.AdminContact;
    /// <summary>Master switch: when false, locking is bypassed entirely and every week stays editable.</summary>
    public bool Enabled => _o.Enabled;

    /// <summary>
    /// Formats a lock deadline for human/model-facing output (<c>yyyy-MM-dd HH:mm zzz</c>).
    /// Module-shared so week_status and plan_week render deadlines identically, and always
    /// invariant so the offset/format never shifts with the host locale.
    /// </summary>
    public static string FormatDeadline(DateTimeOffset dto) =>
        dto.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
}
