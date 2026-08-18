namespace TogglMcp.Planning;

/// <summary>
/// The legacy dedup marker (`⟨sourceUid⟩`) that entries written before the ledger existed still
/// carry in their description. READ-ONLY: nothing appends it any more — descriptions are written
/// exactly as the user approves them, and identity lives in <see cref="ILedgerStore"/> and
/// <see cref="EntryFingerprint"/>. Kept so re-planning a week logged under the old scheme still
/// recognises those entries instead of duplicating them.
/// </summary>
public static class SourceMarker
{
    /// <summary>Rebuilds the ⟨…⟩ marker for a source uid, to look for it in an existing description.</summary>
    public static string For(string sourceUid) => $"⟨{sourceUid}⟩";
}
