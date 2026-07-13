using System.Globalization;
using TogglMcp.Lock;
using TogglMcp.Toggl;

namespace TogglMcp.Features.WeekStatus;

internal sealed class WeekStatusHandler(ITogglClient toggl, WeekLockPolicy locks)
{
    public async Task<WeekStatusResult> HandleAsync(string? weekOf, CancellationToken cancellationToken)
    {
        var anchor = ParseDate(weekOf) ?? locks.Today();
        var weekStart = WeekLockPolicy.WeekStartOf(anchor);
        var weekEnd = weekStart.AddDays(6);
        var lockInfo = locks.GetWeekLock(weekStart);

        var entries = await toggl.GetTimeEntriesAsync(weekStart, weekStart.AddDays(7), cancellationToken);
        var perDay = new Dictionary<DateOnly, double>();
        foreach (var entry in entries)
        {
            if (entry.Duration <= 0) continue; // ignore running/negative entries
            if (!DateTimeOffset.TryParse(entry.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) continue;
            var day = locks.LocalDayOf(dto);
            perDay[day] = perDay.GetValueOrDefault(day) + entry.Duration / 3600.0;
        }

        var days = new List<DayStatus>();
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var working = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var hours = Math.Round(perDay.GetValueOrDefault(day), 2);
            var dayLock = locks.GetWeekLock(day);
            days.Add(new DayStatus(day.ToString("yyyy-MM-dd"), day.DayOfWeek.ToString(), working, hours, hours <= 0, dayLock.Editable, Fmt(dayLock.LocksAt)));
        }

        var emptyWorking = days.Where(d => d.Working && d.Empty).Select(d => d.DayOfWeek).ToList();
        var range = $"{weekStart:MMM d}-{weekEnd:MMM d}";
        var summary = lockInfo.IsLocked
            ? $"Week {range} is LOCKED (closed {lockInfo.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it."
            : emptyWorking.Count == 0
                ? $"Week {range}: all working days filled. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}."
                : $"Week {range}: {emptyWorking.Count} empty working day(s): {string.Join(", ", emptyWorking)}. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}.";

        return new WeekStatusResult(weekStart.ToString("yyyy-MM-dd"), weekEnd.ToString("yyyy-MM-dd"),
            lockInfo.IsLocked, Fmt(lockInfo.LocksAt), days, summary);
    }

    private static string Fmt(DateTimeOffset dto) => dto.ToString("yyyy-MM-dd HH:mm zzz");

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
