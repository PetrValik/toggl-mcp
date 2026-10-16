namespace TogglMcp.Features.WeekStatus;

/// <summary>One calendar day's back-fill state within a week, as surfaced to the model by week_status.</summary>
public sealed record DayStatus(
    string Date,        // ISO yyyy-MM-dd (invariant)
    string DayOfWeek,   // English weekday name (Monday…Sunday)
    bool Working,       // Mon–Fri; weekends are not expected to be filled
    double HoursLogged, // total logged hours for the day, rounded to 2dp
    bool Empty,         // true when HoursLogged <= 0 (nothing to show yet)
    bool Editable,      // false once the day's week has locked — can no longer be written
    string? LocksAt);   // formatted lock deadline for this day's week; null when locking is off

/// <summary>Result of the week_status tool: the week's per-day fill state plus its lock deadline and a model-facing summary.</summary>
public sealed record WeekStatusResult(
    string WeekStart,           // Monday of the week, ISO yyyy-MM-dd
    string WeekEnd,             // Sunday of the week, ISO yyyy-MM-dd
    bool Locked,                // true once the week has permanently closed for edits
    string? LocksAt,            // formatted lock deadline for the whole week; null when locking is off
    IReadOnlyList<DayStatus> Days, // 7 entries, Monday-first
    string Summary);            // one-line human/model summary (empty days, lock state)
