using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Sync;

public class SyncStateTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static SyncState StateAt(SyncStatus status) => status switch
    {
        SyncStatus.Pending => SyncState.Pending,
        SyncStatus.Processing => SyncState.Pending.Claim(),
        SyncStatus.Synced => SyncState.Pending.Claim().MarkSynced(At),
        SyncStatus.Failed => SyncState.Pending.Claim().MarkFailed("boom"),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static SyncState Apply(SyncState state, SyncStatus to) => to switch
    {
        SyncStatus.Processing => state.Claim(),
        SyncStatus.Synced => state.MarkSynced(At),
        SyncStatus.Failed => state.MarkFailed("boom"),
        SyncStatus.Pending => state.Requeue(),
        _ => throw new ArgumentOutOfRangeException(nameof(to)),
    };

    // The full transition table from the spec, section 5.1.
    public static TheoryData<SyncStatus, SyncStatus, bool> Table => new()
    {
        { SyncStatus.Pending, SyncStatus.Processing, true },
        { SyncStatus.Pending, SyncStatus.Synced, false },
        { SyncStatus.Pending, SyncStatus.Failed, true },
        { SyncStatus.Pending, SyncStatus.Pending, false },
        { SyncStatus.Processing, SyncStatus.Processing, false },
        { SyncStatus.Processing, SyncStatus.Synced, true },
        { SyncStatus.Processing, SyncStatus.Failed, true },
        { SyncStatus.Processing, SyncStatus.Pending, true },
        { SyncStatus.Synced, SyncStatus.Processing, false },
        { SyncStatus.Synced, SyncStatus.Synced, true },
        { SyncStatus.Synced, SyncStatus.Failed, false },
        { SyncStatus.Synced, SyncStatus.Pending, true },
        { SyncStatus.Failed, SyncStatus.Processing, false },
        { SyncStatus.Failed, SyncStatus.Synced, false },
        { SyncStatus.Failed, SyncStatus.Failed, false },
        { SyncStatus.Failed, SyncStatus.Pending, true },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Transition_table_is_enforced(SyncStatus from, SyncStatus to, bool allowed)
    {
        var state = StateAt(from);

        if (allowed)
        {
            Assert.Equal(to, Apply(state, to).Status);
        }
        else
        {
            var ex = Assert.Throws<InvalidSyncTransitionException>(() => Apply(state, to));
            Assert.Equal(from, ex.From);
            Assert.Equal(to, ex.To);
        }
    }

    [Fact]
    public void Claim_clears_a_previous_error()
    {
        var requeued = SyncState.Pending.Claim().MarkFailed("boom").Requeue();

        var claimed = requeued.Claim();

        Assert.Null(claimed.Error);
    }

    [Fact]
    public void MarkSynced_records_the_time_and_clears_the_error()
    {
        var synced = SyncState.Pending.Claim().MarkSynced(At);

        Assert.Equal(At, synced.SyncedAt);
        Assert.Null(synced.Error);
    }

    [Fact]
    public void MarkFailed_records_the_error_and_clears_synced_at()
    {
        var failed = SyncState.Pending.Claim().MarkFailed("connection reset");

        Assert.Equal("connection reset", failed.Error);
        Assert.Null(failed.SyncedAt);
    }

    [Fact]
    public void Requeue_resets_error_and_synced_at()
    {
        var requeued = SyncState.Pending.Claim().MarkSynced(At).Requeue();

        Assert.Equal(SyncStatus.Pending, requeued.Status);
        Assert.Null(requeued.Error);
        Assert.Null(requeued.SyncedAt);
    }

    [Fact]
    public void SyncState_is_a_value_equal_by_content()
    {
        Assert.Equal(SyncState.Pending.Claim(), SyncState.Pending.Claim());
    }
}
