using TogglMcp.Planning;

namespace TogglMcp.Tests.Planning;

/// <summary>
/// The fingerprint is what lets dedup work without writing anything into the entry's
/// description: two entries are the same piece of work if they start at the same instant,
/// last the same time, and belong to the same project.
/// </summary>
public sealed class EntryFingerprintTests
{
    [Fact]
    public void The_same_instant_written_in_different_offsets_fingerprints_the_same()
    {
        // Toggl echoes back whatever offset the entry was written with; the plan proposes
        // Prague local time. Both denote the same instant and must collide.
        var prague = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, 123);
        var utc = EntryFingerprint.TryFor("2026-08-17T07:00:00Z", 3600, 123);

        Assert.NotNull(prague);
        Assert.Equal(prague, utc);
    }

    [Fact]
    public void A_different_duration_fingerprints_differently()
    {
        var oneHour = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, 123);
        var twoHours = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 7200, 123);

        Assert.NotEqual(oneHour, twoHours);
    }

    [Fact]
    public void A_different_project_fingerprints_differently()
    {
        var platform = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, 123);
        var website = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, 456);

        Assert.NotEqual(platform, website);
    }

    [Fact]
    public void No_project_is_not_the_same_as_some_project()
    {
        var noProject = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, null);
        var someProject = EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 3600, 123);

        Assert.NotNull(noProject);
        Assert.NotEqual(noProject, someProject);
    }

    [Fact]
    public void An_unparsable_start_has_no_fingerprint()
    {
        // A Toggl entry with a start we can't read must never accidentally match a proposal.
        Assert.Null(EntryFingerprint.TryFor("not a date", 3600, 123));
        Assert.Null(EntryFingerprint.TryFor(null, 3600, 123));
    }

    [Fact]
    public void A_running_entry_has_no_fingerprint()
    {
        // Toggl encodes a still-running entry as a negative duration. It isn't a completed
        // piece of work, so it can never be the duplicate of a planned one.
        Assert.Null(EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", -1755414000, 123));
        Assert.Null(EntryFingerprint.TryFor("2026-08-17T09:00:00+02:00", 0, 123));
    }
}
