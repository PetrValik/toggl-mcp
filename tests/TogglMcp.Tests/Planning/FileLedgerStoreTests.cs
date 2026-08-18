using TogglMcp.Planning;
using TogglMcp.Tests.Support;

namespace TogglMcp.Tests.Planning;

/// <summary>
/// The ledger is what remembers "this sourceUid became that Toggl entry" now that nothing is
/// written into the entry's description. It has to survive a restart, stay per-user, and not
/// grow forever.
/// </summary>
public sealed class FileLedgerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_recorded_entry_reads_back_for_its_day()
    {
        using var dir = new TempDataDir();
        var store = new FileLedgerStore(dir.Options, new MutableTimeProvider(Now));

        await store.RecordAsync("user-1", "calevt:AB12", entryId: 991, new DateOnly(2026, 8, 17), CancellationToken.None);
        var records = await store.GetAsync("user-1", new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 17), CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal("calevt:AB12", record.SourceUid);
        Assert.Equal(991, record.EntryId);
        Assert.Equal(new DateOnly(2026, 8, 17), record.Day);
    }

    [Fact]
    public async Task A_record_survives_a_restart_of_the_store()
    {
        using var dir = new TempDataDir();
        var clock = new MutableTimeProvider(Now);
        await new FileLedgerStore(dir.Options, clock)
            .RecordAsync("user-1", "calevt:AB12", 991, new DateOnly(2026, 8, 17), CancellationToken.None);

        // A brand-new store over the same directory is what a restarted container sees.
        var records = await new FileLedgerStore(dir.Options, clock)
            .GetAsync("user-1", new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 17), CancellationToken.None);

        Assert.Equal("calevt:AB12", Assert.Single(records).SourceUid);
    }

    [Fact]
    public async Task Records_outside_the_requested_range_are_not_returned()
    {
        using var dir = new TempDataDir();
        var store = new FileLedgerStore(dir.Options, new MutableTimeProvider(Now));
        await store.RecordAsync("user-1", "in", 1, new DateOnly(2026, 8, 17), CancellationToken.None);
        await store.RecordAsync("user-1", "before", 2, new DateOnly(2026, 8, 10), CancellationToken.None);
        await store.RecordAsync("user-1", "after", 3, new DateOnly(2026, 8, 20), CancellationToken.None);

        var records = await store.GetAsync("user-1", new DateOnly(2026, 8, 16), new DateOnly(2026, 8, 18), CancellationToken.None);

        Assert.Equal("in", Assert.Single(records).SourceUid);
    }

    [Fact]
    public async Task One_users_ledger_is_invisible_to_another()
    {
        using var dir = new TempDataDir();
        var store = new FileLedgerStore(dir.Options, new MutableTimeProvider(Now));
        await store.RecordAsync("user-1", "calevt:AB12", 991, new DateOnly(2026, 8, 17), CancellationToken.None);

        var otherUser = await store.GetAsync("user-2", new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 17), CancellationToken.None);

        Assert.Empty(otherUser);
    }

    [Fact]
    public async Task A_user_with_no_ledger_yet_reads_as_empty()
    {
        using var dir = new TempDataDir();
        var store = new FileLedgerStore(dir.Options, new MutableTimeProvider(Now));

        var records = await store.GetAsync("nobody", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), CancellationToken.None);

        Assert.Empty(records);
    }

    [Fact]
    public async Task Re_recording_a_sourceUid_replaces_the_previous_entry_id()
    {
        // Deleting an entry in Toggl and letting the plan write it again must leave one record,
        // pointing at the entry that actually exists now.
        using var dir = new TempDataDir();
        var store = new FileLedgerStore(dir.Options, new MutableTimeProvider(Now));
        await store.RecordAsync("user-1", "calevt:AB12", 991, new DateOnly(2026, 8, 17), CancellationToken.None);

        await store.RecordAsync("user-1", "calevt:AB12", 1042, new DateOnly(2026, 8, 17), CancellationToken.None);

        var record = Assert.Single(await store.GetAsync("user-1", new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 17), CancellationToken.None));
        Assert.Equal(1042, record.EntryId);
    }

    [Fact]
    public async Task Records_older_than_the_retention_horizon_are_dropped_on_the_next_write()
    {
        using var dir = new TempDataDir();
        var clock = new MutableTimeProvider(Now);
        var store = new FileLedgerStore(dir.Options, clock);
        var ancientDay = new DateOnly(2026, 8, 17);
        await store.RecordAsync("user-1", "ancient", 1, ancientDay, CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(FileLedgerStore.RetentionDays + 1));
        var freshDay = DateOnly.FromDateTime(clock.Now.UtcDateTime);
        await store.RecordAsync("user-1", "fresh", 2, freshDay, CancellationToken.None);

        var all = await store.GetAsync("user-1", ancientDay, freshDay, CancellationToken.None);
        Assert.Equal("fresh", Assert.Single(all).SourceUid);
    }
}
