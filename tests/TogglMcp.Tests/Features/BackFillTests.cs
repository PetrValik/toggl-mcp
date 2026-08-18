using Microsoft.Extensions.Options;
using TogglMcp.Configuration;
using TogglMcp.Features.CommitPlan;
using TogglMcp.Features.PlanWeek;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Tests.Support;

namespace TogglMcp.Tests.Features;

/// <summary>
/// plan_week and commit_plan across the boundary that matters to the user: what actually lands in
/// their timesheet, and whether running the same plan twice can ever log the same work twice.
/// </summary>
public sealed class BackFillTests : IDisposable
{
    private const string Description = "Refactoring the auth pipeline";
    private const string Start = "2026-08-17T09:00:00+02:00";

    private readonly TempDataDir _dir = new();
    private readonly FakeTogglClient _toggl = new();
    private readonly FakeTokenProvider _tokens = new(Guid.NewGuid().ToString("N")); // isolate the per-user commit gate
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPlanStore _plans;
    private readonly FileLedgerStore _ledger;
    private readonly WeekLockPolicy _locks;

    public BackFillTests()
    {
        _plans = new InMemoryPlanStore(_clock);
        _ledger = new FileLedgerStore(_dir.Options, _clock);
        // Locking is exercised by its own policy tests; these are about what gets written.
        _locks = new WeekLockPolicy(new LockOptions { Enabled = false, TimeZone = "Europe/Prague" }, _clock);
    }

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private PlanWeekHandler Planner() => new(_toggl, _tokens, _locks, _plans, _ledger, _clock);

    private CommitPlanHandler Committer() => new(_toggl, _tokens, _locks, _plans, _ledger, Options.Create(new TogglOptions()));

    private static PlanEntryInput Proposal(string sourceUid = "calevt:AB12", string description = Description, string start = Start, long duration = 3600, long? projectId = 123) =>
        new() { SourceUid = sourceUid, Description = description, Start = start, DurationSeconds = duration, ProjectId = projectId };

    [Fact]
    public async Task The_preview_shows_the_description_the_user_proposed_and_nothing_else()
    {
        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(Description, entry.Description);
    }

    [Fact]
    public async Task The_entry_written_to_toggl_carries_the_users_description_verbatim()
    {
        // The whole point of the change: nothing is appended to what the user approved.
        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);
        await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        var written = Assert.Single(_toggl.Entries);
        Assert.Equal(Description, written.Description);
    }

    [Fact]
    public async Task Committing_records_the_entry_in_the_ledger()
    {
        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);
        var commit = await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        var recorded = Assert.Single(await _ledger.GetAsync(_tokens.UserKey, new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 17)));
        Assert.Equal("calevt:AB12", recorded.SourceUid);
        Assert.Equal(Assert.Single(commit.Results).EntryId, recorded.EntryId);
    }

    [Fact]
    public async Task Re_planning_the_same_work_after_a_commit_reports_it_as_already_logged()
    {
        var first = await Planner().HandleAsync([Proposal()], CancellationToken.None);
        await Committer().HandleAsync(first.PlanId!, null, CancellationToken.None);

        var second = await Planner().HandleAsync([Proposal()], CancellationToken.None);

        Assert.Equal(PlannedEntry.StatusDuplicate, Assert.Single(second.Entries).Status);
        Assert.Null(second.PlanId);
    }

    [Fact]
    public async Task Committing_the_same_plan_twice_writes_the_entry_once()
    {
        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);
        await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        var second = await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Single(_toggl.Entries);
    }

    [Fact]
    public async Task Two_proposals_sharing_a_sourceUid_write_only_one_entry()
    {
        var plan = await Planner().HandleAsync(
            [Proposal(), Proposal(description: "Same id, different words", start: "2026-08-17T14:00:00+02:00")],
            CancellationToken.None);
        await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        Assert.Single(_toggl.Entries);
    }

    [Fact]
    public async Task Work_already_logged_by_hand_is_recognised_without_any_marker()
    {
        // Same start, length and project, logged in the Toggl UI with the user's own wording.
        _toggl.Seed(Start, 3600, 123, "auth pipeline — refactor");

        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);

        Assert.Equal(PlannedEntry.StatusDuplicate, Assert.Single(plan.Entries).Status);
    }

    [Fact]
    public async Task An_entry_written_before_the_ledger_existed_is_still_recognised()
    {
        // Legacy: written while the ⟨sourceUid⟩ marker was still embedded. Different times, so
        // only the marker can identify it.
        _toggl.Seed("2026-08-17T13:00:00+02:00", 5400, 456, $"Something else {SourceMarker.For("calevt:AB12")}");

        var plan = await Planner().HandleAsync([Proposal()], CancellationToken.None);

        Assert.Equal(PlannedEntry.StatusDuplicate, Assert.Single(plan.Entries).Status);
    }

    [Fact]
    public async Task An_entry_deleted_in_toggl_can_be_written_again()
    {
        var first = await Planner().HandleAsync([Proposal()], CancellationToken.None);
        var commit = await Committer().HandleAsync(first.PlanId!, null, CancellationToken.None);
        _toggl.Delete(Assert.Single(commit.Results).EntryId!.Value);

        var second = await Planner().HandleAsync([Proposal()], CancellationToken.None);

        Assert.Equal(PlannedEntry.StatusNew, Assert.Single(second.Entries).Status);
        Assert.NotNull(second.PlanId);
    }

    [Fact]
    public async Task A_ledger_that_cannot_be_written_degrades_instead_of_failing_the_commit()
    {
        // The entry is already in Toggl by the time the ledger write fails. Abandoning the commit
        // would strand the remaining entries and hide the one that landed; dedup still has content
        // matching to fall back on, so the run continues and says what was lost.
        var plan = await Planner().HandleAsync(
            [Proposal(), Proposal(sourceUid: "note:standup", description: "Standup", start: "2026-08-17T11:00:00+02:00", duration: 900, projectId: 456)],
            CancellationToken.None);
        var committer = new CommitPlanHandler(_toggl, _tokens, _locks, _plans, new BrokenLedgerStore(), Options.Create(new TogglOptions()));

        var commit = await committer.HandleAsync(plan.PlanId!, null, CancellationToken.None);

        Assert.Equal(2, commit.Created);
        Assert.Equal(2, _toggl.Entries.Count);
        Assert.All(commit.Results, r => Assert.Contains("ledger", r.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Different_work_on_the_same_day_is_still_written()
    {
        var plan = await Planner().HandleAsync(
            [Proposal(), Proposal(sourceUid: "note:standup", description: "Standup", start: "2026-08-17T11:00:00+02:00", duration: 900, projectId: 456)],
            CancellationToken.None);
        var commit = await Committer().HandleAsync(plan.PlanId!, null, CancellationToken.None);

        Assert.Equal(2, commit.Created);
        Assert.Equal(2, _toggl.Entries.Count);
        Assert.All(_toggl.Entries, e => Assert.DoesNotContain("⟨", e.Description));
    }
}
