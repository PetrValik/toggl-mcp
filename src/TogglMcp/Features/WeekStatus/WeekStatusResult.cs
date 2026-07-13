namespace TogglMcp.Features.WeekStatus;

public sealed record DayStatus(
    string Date,
    string DayOfWeek,
    bool Working,
    double HoursLogged,
    bool Empty,
    bool Editable,
    string? LocksAt);

public sealed record WeekStatusResult(
    string WeekStart,
    string WeekEnd,
    bool Locked,
    string? LocksAt,
    IReadOnlyList<DayStatus> Days,
    string Summary);
