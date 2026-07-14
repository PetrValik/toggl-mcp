using System.Globalization;
using TogglMcp.Contracts;
using TogglMcp.Lock;
using TogglMcp.Toggl;
using static System.FormattableString;

namespace TogglMcp.Features.WeekStatus;

/// <summary>
/// Sole logic layer for week_status: resolves the target week, aggregates Toggl hours per day,
/// and reports each day's fill/lock state plus a model-facing summary. Read-only — never writes.
/// </summary>
internal sealed class WeekStatusHandler(ITogglClient toggl, WeekLockPolicy locks)
{
    /// <summary>
    /// Builds the week's status: pulls Toggl entries for the Mon–Sun week containing <paramref name="weekOf"/>
    /// (or the current week), tallies hours per local day, and returns per-day fill + lock info and a summary.
    /// </summary>
    /// <param name="weekOf">Any date in the target week; null/unparseable falls back to today in the lock time zone.</param>
    public async Task<WeekStatusResult> HandleAsync(string? weekOf, CancellationToken cancellationToken)
    {
        // Resolve the anchor date (fall back to today) and normalise to the Monday of its week.
        var anchor = DateInput.Parse(weekOf) ?? locks.Today();
        var weekStart = WeekLockPolicy.WeekStartOf(anchor);
        var weekEnd = weekStart.AddDays(6);
        var lockInfo = locks.GetWeekLock(weekStart);

        // Fetch the week's entries (end is exclusive: weekStart..weekStart+7 covers Mon–Sun) and tally hours per day.
        var entries = await toggl.GetTimeEntriesAsync(weekStart, weekStart.AddDays(7), cancellationToken);
        var perDay = new Dictionary<DateOnly, double>();
        foreach (var entry in entries)
        {
            if (entry.Duration <= 0) continue; // ignore running/negative entries
            if (!DateTimeOffset.TryParse(entry.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) continue;
            var day = locks.LocalDayOf(dto); // bucket by local calendar day, not UTC
            perDay[day] = perDay.GetValueOrDefault(day) + entry.Duration / 3600.0; // Toggl duration is seconds -> hours
        }

        // Materialise all 7 days (Monday-first) with per-day hours, working flag and lock state.
        var days = new List<DayStatus>();
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var working = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var hours = Math.Round(perDay.GetValueOrDefault(day), 2);
            var dayLock = locks.GetWeekLock(day);
            days.Add(new DayStatus(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day.DayOfWeek.ToString(), working, hours, hours <= 0, dayLock.Editable, WeekLockPolicy.FormatDeadline(dayLock.LocksAt)));
        }

        // Working days with no hours are what still needs back-filling; drive the summary off them.
        var emptyWorking = days.Where(d => d.Working && d.Empty).Select(d => d.DayOfWeek).ToList();
        var range = Invariant($"{weekStart:MMM d}–{weekEnd:MMM d}");
        // Summary branches: locked week -> point at admin; no empties -> all-filled; else list the empty days.
        var summary = lockInfo.IsLocked
            ? Invariant($"Week {range} is LOCKED (closed {lockInfo.LocksAt:yyyy-MM-dd HH:mm}); ask {locks.AdminContact} to reopen it.")
            : emptyWorking.Count == 0
                ? Invariant($"Week {range}: all working days filled. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}.")
                : Invariant($"Week {range}: {emptyWorking.Count} empty working day(s): {string.Join(", ", emptyWorking)}. Locks {lockInfo.LocksAt:yyyy-MM-dd HH:mm}.");

        return new WeekStatusResult(weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), weekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            lockInfo.IsLocked, WeekLockPolicy.FormatDeadline(lockInfo.LocksAt), days, summary);
    }
}
