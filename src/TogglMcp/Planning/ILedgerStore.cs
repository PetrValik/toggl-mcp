namespace TogglMcp.Planning;

/// <summary>
/// One committed entry as the server remembers it: the plan's stable <paramref name="SourceUid"/>
/// and the Toggl entry it became.
/// </summary>
/// <param name="SourceUid">Stable id of the originating source item, as the plan proposed it.</param>
/// <param name="EntryId">Id of the Toggl entry that was created for it.</param>
/// <param name="Day">Local calendar day the entry counts for; what range queries filter on.</param>
/// <param name="CreatedAt">When the server recorded it (diagnostics only).</param>
public sealed record LedgerRecord(string SourceUid, long EntryId, DateOnly Day, DateTimeOffset CreatedAt);

/// <summary>
/// Durable per-user record of what this server has already written to Toggl.
///
/// It replaces the <c>⟨sourceUid⟩</c> marker that used to be appended to every entry's
/// description: the mapping now lives here instead of in the user's timesheet, so descriptions
/// read exactly as the user approved them.
/// </summary>
public interface ILedgerStore
{
    /// <summary>Records that <paramref name="sourceUid"/> was written as Toggl entry <paramref name="entryId"/>. Last write wins per sourceUid, so re-logging a deleted entry updates the record rather than adding a second one.</summary>
    Task RecordAsync(string userKey, string sourceUid, long entryId, DateOnly day, CancellationToken ct = default);

    /// <summary>Records for the user whose <see cref="LedgerRecord.Day"/> falls in the inclusive range; empty for an unknown user.</summary>
    Task<IReadOnlyList<LedgerRecord>> GetAsync(string userKey, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);
}
