using System.Text.Json.Serialization;

namespace TogglMcp.Configuration;

/// <summary>
/// Timesheet lock policy, for workspaces whose admin locks weeks permanently: at
/// <see cref="CloseDayOfWeek"/> <see cref="CloseTime"/>, the week that ended
/// <see cref="GraceWeeks"/> week(s) earlier closes and can never be edited again (only the
/// admin can reopen it). When enabled, the server never attempts to write into a locked
/// week — it refuses cleanly instead. Off by default: most workspaces never lock.
/// </summary>
public sealed class LockOptions
{
    /// <summary>Configuration section these options bind from (appsettings / env).</summary>
    public const string SectionName = "Lock";

    /// <summary>When false, nothing is ever considered locked (e.g. a workspace without locking).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// IANA time zone that defines day and week boundaries (and the lock clock). Blank = the
    /// machine's own time zone.
    /// </summary>
    public string TimeZone { get; set; } = "";

    /// <summary>Day of week the lock fires.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DayOfWeek CloseDayOfWeek { get; set; } = DayOfWeek.Friday;

    /// <summary>Local time on <see cref="CloseDayOfWeek"/> when the lock fires.</summary>
    public TimeSpan CloseTime { get; set; } = new(23, 59, 0);

    /// <summary>
    /// How many weeks a week stays editable. 1 = a week locks on the close-day of the
    /// following week (i.e. "the previous week closes each Friday 23:59").
    /// </summary>
    public int GraceWeeks { get; set; } = 1;

    /// <summary>Who to contact to reopen a locked week (surfaced in refusal messages).</summary>
    public string AdminContact { get; set; } = "your workspace admin";
}
