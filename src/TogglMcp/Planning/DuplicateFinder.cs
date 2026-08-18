using System.Globalization;
using TogglMcp.Toggl;

namespace TogglMcp.Planning;

/// <summary>An entry that already covers a proposal.</summary>
/// <param name="EntryId">The Toggl entry it collides with, when known.</param>
/// <param name="Reason">User-facing explanation, surfaced as the plan entry's note.</param>
public sealed record DuplicateMatch(long? EntryId, string Reason);

/// <summary>
/// Decides whether a proposed entry is already in Toggl, over one day range's worth of context.
/// Module-shared because both slices must reach the same verdict — PlanWeek to classify the
/// preview, CommitPlan to re-check immediately before writing.
///
/// Three independent signals, most precise first:
/// <list type="number">
/// <item>the <see cref="ILedgerStore"/> says we wrote this sourceUid, and that entry still exists;</item>
/// <item>a live entry has the same content (<see cref="EntryFingerprint"/>) — catches entries
/// logged by hand, and re-runs where the model regenerated the sourceUid;</item>
/// <item>a live entry's description carries the legacy <c>⟨sourceUid⟩</c> marker, for everything
/// written before the ledger existed.</item>
/// </list>
/// A ledger record whose entry is gone from Toggl is deliberately ignored: the user deleted it,
/// so it may be written again.
/// </summary>
public sealed class DuplicateFinder
{
    // Live Toggl entries for the day range, indexed by id — the snapshot the verdict is checked against.
    private readonly Dictionary<long, TogglTimeEntry> _live;
    // sourceUid -> entry id, from the ledger plus anything remembered during this run.
    private readonly Dictionary<string, long> _byUid;
    // fingerprint -> entry id, from the live entries plus anything remembered during this run.
    private readonly Dictionary<string, long> _byFingerprint;

    /// <summary>
    /// Builds the finder over a day range's live Toggl entries and the user's ledger records for
    /// the same range.
    /// </summary>
    public DuplicateFinder(IReadOnlyList<TogglTimeEntry> liveEntries, IReadOnlyList<LedgerRecord> ledger)
    {
        _live = liveEntries.ToDictionary(e => e.Id);
        _byFingerprint = [];
        foreach (var entry in liveEntries)
        {
            var fingerprint = EntryFingerprint.TryFor(entry.Start, entry.Duration, entry.ProjectId);
            // Running entries (negative duration) and unparsable starts have no fingerprint and
            // therefore can't collide with anything.
            if (fingerprint is not null) _byFingerprint.TryAdd(fingerprint, entry.Id);
        }

        _byUid = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var record in ledger)
        {
            // Only trust a ledger record whose entry Toggl still has.
            if (_live.ContainsKey(record.EntryId)) _byUid[record.SourceUid] = record.EntryId;
        }
    }

    /// <summary>
    /// The entry that already covers this proposal, or null if it is genuinely new.
    /// </summary>
    /// <param name="sourceUid">Stable id of the proposal's source item.</param>
    /// <param name="startRfc3339">Proposed start, with an explicit offset.</param>
    /// <param name="durationSeconds">Proposed length in seconds.</param>
    /// <param name="projectId">Resolved project id, or null for "No Project".</param>
    public DuplicateMatch? Find(string sourceUid, string? startRfc3339, long durationSeconds, long? projectId)
    {
        if (_byUid.TryGetValue(sourceUid, out var knownId))
            return new DuplicateMatch(knownId, Invariant($"already logged (entry {knownId})"));

        var fingerprint = EntryFingerprint.TryFor(startRfc3339, durationSeconds, projectId);
        if (fingerprint is not null && _byFingerprint.TryGetValue(fingerprint, out var sameContentId))
            return new DuplicateMatch(sameContentId,
                Invariant($"an entry with the same start, length and project is already logged (entry {sameContentId})"));

        // Legacy: entries written while the ⟨sourceUid⟩ marker was still embedded in descriptions.
        var marker = SourceMarker.For(sourceUid);
        foreach (var entry in _live.Values)
        {
            if (entry.Description?.Contains(marker, StringComparison.Ordinal) == true)
                return new DuplicateMatch(entry.Id, Invariant($"already logged (entry {entry.Id})"));
        }

        return null;
    }

    /// <summary>
    /// Registers an entry written during this run. The live-entry snapshot was taken before the
    /// run started, so without this a commit could write the same work twice in one pass.
    /// </summary>
    public void Remember(string sourceUid, long entryId, string? startRfc3339, long durationSeconds, long? projectId)
    {
        _byUid[sourceUid] = entryId;
        var fingerprint = EntryFingerprint.TryFor(startRfc3339, durationSeconds, projectId);
        if (fingerprint is not null) _byFingerprint[fingerprint] = entryId;
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
