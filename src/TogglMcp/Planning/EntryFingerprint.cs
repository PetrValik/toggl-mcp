using System.Globalization;

namespace TogglMcp.Planning;

/// <summary>
/// Content identity of a completed time entry: the instant it starts, how long it lasts, and
/// which project it belongs to. Module-shared because both slices dedup with it — PlanWeek to
/// classify a proposal, CommitPlan to re-check at write time — and they must agree byte for byte.
///
/// This exists so dedup needs nothing written into the entry's description: the entry as Toggl
/// stores it already carries its own identity.
/// </summary>
public static class EntryFingerprint
{
    /// <summary>
    /// Fingerprint of a completed entry, or null when it has none — an unreadable start, or a
    /// non-positive duration (Toggl encodes a still-running entry as a negative duration, and a
    /// running entry is not a completed piece of work that a plan could duplicate).
    /// </summary>
    /// <param name="startRfc3339">Start as Toggl returns it / as the plan proposes it; any offset.</param>
    /// <param name="durationSeconds">Entry length in seconds; must be positive.</param>
    /// <param name="projectId">Resolved Toggl project id, or null for "No Project".</param>
    public static string? TryFor(string? startRfc3339, long durationSeconds, long? projectId)
    {
        if (durationSeconds <= 0) return null;
        if (!DateTimeOffset.TryParse(startRfc3339, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return null;
        return For(start, durationSeconds, projectId);
    }

    /// <summary>
    /// Fingerprint of an already-parsed start. Normalised to UTC so the same instant written
    /// with different offsets (Prague local from the plan, whatever Toggl echoes back) collides.
    /// </summary>
    public static string For(DateTimeOffset start, long durationSeconds, long? projectId) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{start.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}|{durationSeconds}|{(projectId is { } id ? id : -1)}");
}
