using System.Globalization;

namespace TogglMcp.Contracts;

/// <summary>
/// Parses a user-supplied <c>YYYY-MM-DD</c> date argument. Module-shared so the read slices
/// that accept optional date inputs (list_entries, week_status) interpret them identically
/// — always with the invariant culture, never the host locale.
/// </summary>
public static class DateInput
{
    /// <summary>
    /// Parses <paramref name="value"/> as an invariant-culture date, returning null for null/blank/unparseable input
    /// rather than throwing — callers treat a null as "no date supplied" and fall back to their own default.
    /// </summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
