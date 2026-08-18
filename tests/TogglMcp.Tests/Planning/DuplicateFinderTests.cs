using TogglMcp.Planning;
using TogglMcp.Toggl;

namespace TogglMcp.Tests.Planning;

/// <summary>
/// One place decides "is this already in Toggl?", so plan_week's preview and commit_plan's
/// write-time re-check can never disagree. Three independent signals, in order of precision:
/// the ledger, the entry's own content, and — for entries written before the ledger existed —
/// the legacy ⟨sourceUid⟩ marker.
/// </summary>
public sealed class DuplicateFinderTests
{
    private const string Start = "2026-08-17T09:00:00+02:00";
    private static readonly DateTimeOffset Recorded = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    private static TogglTimeEntry Entry(long id, string start = Start, long duration = 3600, long? projectId = 123, string? description = "Refactoring the auth pipeline") =>
        new() { Id = id, WorkspaceId = 1, Start = start, Duration = duration, ProjectId = projectId, Description = description };

    [Fact]
    public void A_ledger_hit_whose_entry_still_exists_is_a_duplicate()
    {
        var finder = new DuplicateFinder(
            liveEntries: [Entry(991)],
            ledger: [new LedgerRecord("calevt:AB12", 991, new DateOnly(2026, 8, 17), Recorded)]);

        // Deliberately different content from the live entry: the ledger alone must settle it,
        // so an entry the user edited by hand in Toggl is still recognised as already logged.
        var match = finder.Find("calevt:AB12", "2026-08-17T14:00:00+02:00", 1800, 456);

        Assert.NotNull(match);
        Assert.Equal(991, match.EntryId);
    }

    [Fact]
    public void A_ledger_hit_whose_entry_was_deleted_in_toggl_is_not_a_duplicate()
    {
        // The user deleted the entry and wants it back. The ledger remembers writing it; Toggl
        // says otherwise, and Toggl is the truth.
        var finder = new DuplicateFinder(
            liveEntries: [],
            ledger: [new LedgerRecord("calevt:AB12", 991, new DateOnly(2026, 8, 17), Recorded)]);

        Assert.Null(finder.Find("calevt:AB12", Start, 3600, 123));
    }

    [Fact]
    public void An_entry_with_the_same_start_length_and_project_is_a_duplicate()
    {
        // Nothing in the ledger — the entry was logged by hand, or under a different sourceUid.
        var finder = new DuplicateFinder(liveEntries: [Entry(991)], ledger: []);

        var match = finder.Find("note:regenerated-uid", Start, 3600, 123);

        Assert.NotNull(match);
        Assert.Equal(991, match.EntryId);
        Assert.Contains("same start", match.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_legacy_marker_in_a_description_still_counts_as_a_duplicate()
    {
        // Entries written before this change carry ⟨sourceUid⟩ in their description. Re-planning
        // that week must not double-log them just because the ledger predates them.
        var legacy = Entry(991, start: "2026-08-17T11:00:00+02:00", duration: 5400,
            description: "Refactoring the auth pipeline ⟨calevt:AB12⟩");
        var finder = new DuplicateFinder(liveEntries: [legacy], ledger: []);

        var match = finder.Find("calevt:AB12", Start, 3600, 123);

        Assert.NotNull(match);
        Assert.Equal(991, match.EntryId);
    }

    [Fact]
    public void An_unrelated_entry_on_the_same_day_is_not_a_duplicate()
    {
        var finder = new DuplicateFinder(
            liveEntries: [Entry(991, start: "2026-08-17T14:00:00+02:00", duration: 1800, projectId: 456)],
            ledger: []);

        Assert.Null(finder.Find("calevt:AB12", Start, 3600, 123));
    }

    [Fact]
    public void A_still_running_entry_never_matches_a_planned_one()
    {
        // Toggl encodes a running entry as a negative duration; it is not a completed piece of
        // work and must not block writing the planned one.
        var running = Entry(991, duration: -1755414000, description: null);
        var finder = new DuplicateFinder(liveEntries: [running], ledger: []);

        Assert.Null(finder.Find("calevt:AB12", Start, 3600, 123));
    }

    [Fact]
    public void An_entry_written_earlier_in_this_run_is_a_duplicate_by_its_sourceUid()
    {
        // The live-entry list is a snapshot taken before the run started, so a commit that
        // writes twice within one run has only this to go on.
        var finder = new DuplicateFinder(liveEntries: [], ledger: []);
        finder.Remember("calevt:AB12", 991, Start, 3600, 123);

        var match = finder.Find("calevt:AB12", Start, 3600, 123);

        Assert.NotNull(match);
        Assert.Equal(991, match.EntryId);
    }

    [Fact]
    public void An_entry_written_earlier_in_this_run_is_a_duplicate_by_its_content_too()
    {
        var finder = new DuplicateFinder(liveEntries: [], ledger: []);
        finder.Remember("calevt:AB12", 991, Start, 3600, 123);

        var match = finder.Find("note:a-different-uid", Start, 3600, 123);

        Assert.NotNull(match);
        Assert.Equal(991, match.EntryId);
    }
}
