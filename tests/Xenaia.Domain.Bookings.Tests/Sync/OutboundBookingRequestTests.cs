using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Sync;

public class OutboundBookingRequestTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForCreate_stores_payload_kind_and_starts_pending()
    {
        var json = "{\"name\":\"test\"}";

        var req = OutboundBookingRequest.ForCreate(json);

        Assert.Equal(OutboundBookingKind.Create, req.Kind);
        Assert.Equal(json, req.Payload);
        Assert.Equal(SyncStatus.Pending, req.Sync.Status);
    }

    [Fact]
    public void ForCreate_with_empty_payload_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCreate(""));
    }

    [Fact]
    public void ForCreate_with_whitespace_payload_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCreate("   "));
    }

    [Fact]
    public void ForCreate_with_null_payload_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCreate(null!));
    }

    [Fact]
    public void ForCancel_stores_payload_kind_and_starts_pending()
    {
        var code = "BK123456";

        var req = OutboundBookingRequest.ForCancel(code);

        Assert.Equal(OutboundBookingKind.Cancel, req.Kind);
        Assert.Equal(code, req.Payload);
        Assert.Equal(SyncStatus.Pending, req.Sync.Status);
    }

    [Fact]
    public void ForCancel_with_empty_code_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCancel(""));
    }

    [Fact]
    public void ForCancel_with_whitespace_code_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCancel("   "));
    }

    [Fact]
    public void ForCancel_with_null_code_throws()
    {
        Assert.Throws<ArgumentException>(() => OutboundBookingRequest.ForCancel(null!));
    }

    [Fact]
    public void ClaimForSync_transitions_to_processing()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");

        req.ClaimForSync();

        Assert.Equal(SyncStatus.Processing, req.Sync.Status);
    }

    [Fact]
    public void MarkSynced_transitions_to_synced_and_records_time()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");
        req.ClaimForSync();

        req.MarkSynced(At);

        Assert.Equal(SyncStatus.Synced, req.Sync.Status);
        Assert.Equal(At, req.Sync.SyncedAt);
    }

    [Fact]
    public void MarkSyncFailed_transitions_to_failed_and_records_error()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");
        req.ClaimForSync();

        req.MarkSyncFailed("connection refused", At);

        Assert.Equal(SyncStatus.Failed, req.Sync.Status);
        Assert.Equal("connection refused", req.Sync.Error);
    }

    [Fact]
    public void RequeueSync_transitions_back_to_pending()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");
        req.ClaimForSync();
        req.MarkSyncFailed("error", At);

        req.RequeueSync();

        Assert.Equal(SyncStatus.Pending, req.Sync.Status);
    }

    [Fact]
    public void Legal_transition_chain_pending_to_processing_to_synced()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");

        req.ClaimForSync();
        Assert.Equal(SyncStatus.Processing, req.Sync.Status);

        req.MarkSynced(At);
        Assert.Equal(SyncStatus.Synced, req.Sync.Status);
    }

    [Fact]
    public void Illegal_transition_from_pending_to_synced_throws()
    {
        var req = OutboundBookingRequest.ForCreate("{\"test\":true}");

        var ex = Assert.Throws<InvalidSyncTransitionException>(() => req.MarkSynced(At));

        Assert.Equal(SyncStatus.Pending, ex.From);
        Assert.Equal(SyncStatus.Synced, ex.To);
    }
}
