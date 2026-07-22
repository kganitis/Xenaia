using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core.Notifications;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Availability;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Claims one outbound booking request and pushes it to the booking system
/// with retry (spec 6.4 step 2). Create: <see cref="IBookingSystemProvider"/>
/// creates the booking, the confirmed snapshot is ingested as Outbound, and
/// the request is marked Synced (the local Booking is born here, not at
/// enqueue). Cancel: the vendor is cancelled first, then the local aggregate
/// is cancelled and saved, and the request is marked Synced. A vendor
/// not-found is a permanent failure (no retries); a retryable failure that
/// exhausts its attempts marks the request Failed with a truncated error. Both
/// failure modes fan out one human-actionable notification. Scoped: the push
/// service resolves a fresh instance per drain cycle.
/// </summary>
public sealed class BookingPusher
{
    private const int MaxErrorLength = 2000;

    private readonly IOutboundBookingRequestStore _store;
    private readonly IBookingSystemProvider _provider;
    private readonly BookingIngestService _ingestService;
    private readonly IBookingStore _bookingStore;
    private readonly BookingChannel _channel;
    private readonly INotificationService _notifications;
    private readonly SyncOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<BookingPusher> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer;

    public BookingPusher(
        IOutboundBookingRequestStore store,
        IBookingSystemProvider provider,
        BookingIngestService ingestService,
        IBookingStore bookingStore,
        BookingChannel channel,
        INotificationService notifications,
        IOptions<SyncOptions> options,
        TimeProvider clock,
        ILogger<BookingPusher> logger,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        _store = store;
        _provider = provider;
        _ingestService = ingestService;
        _bookingStore = bookingStore;
        _channel = channel;
        _notifications = notifications;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _delayer = delayer ?? ((delay, token) => Task.Delay(delay, clock, token));
    }

    /// <summary>Claims and pushes one request; returns the outcome.</summary>
    public async Task<PushOutcome> ProcessAsync(int requestId, CancellationToken ct)
    {
        if (!await _store.TryClaimAsync(requestId, ct))
        {
            _logger.LogDebug(
                "Booking request {Id}: already claimed or not Pending; nothing to push", requestId);
            return PushOutcome.LostClaim;
        }

        var request = await _store.GetByIdAsync(requestId, ct);
        if (request is null)
        {
            _logger.LogWarning("Booking request {Id}: claimed but no longer loadable", requestId);
            return PushOutcome.LostClaim;
        }

        // Same trap as AvailabilityPusher: the atomic claim is a set-based DB
        // update that does not refresh a tracked instance, so a freshly loaded
        // row can still read Pending after the claim. Reconcile in-memory to
        // Processing so the state machine and the row agree on the terminal move.
        if (request.Sync.Status == SyncStatus.Pending)
            request.ClaimForSync();

        try
        {
            switch (request.Kind)
            {
                case OutboundBookingKind.Create:
                    await PushCreateAsync(request, ct);
                    break;
                case OutboundBookingKind.Cancel:
                    await PushCancelAsync(request, ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failedAt = _clock.GetUtcNow();
            var error = Truncate(ex.Message);
            request.MarkSyncFailed(error, failedAt);
            await _store.SaveChangesAsync(ct);
            await NotifyFailureAsync(request, error, ct);
            _logger.LogWarning(
                ex, "Booking request {Id} ({Kind}): push failed; marked Failed", request.Id, request.Kind);
            return PushOutcome.Failed;
        }

        var syncedAt = _clock.GetUtcNow();
        request.MarkSynced(syncedAt);
        await _store.SaveChangesAsync(ct);
        _logger.LogInformation("Booking request {Id} ({Kind}): pushed and marked Synced", request.Id, request.Kind);
        return PushOutcome.Synced;
    }

    /// <summary>Startup recovery: reset stuck Processing rows to Pending, then
    /// re-enqueue every Pending row. Returns the number re-enqueued.</summary>
    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        var reset = await _store.ResetProcessingAsync(ct);

        // One wide fetch: enqueuing leaves rows Pending, so a batched re-read
        // would see the same rows again. At-least-once delivery makes a
        // duplicate enqueue harmless (the claim de-duplicates).
        var pending = await _store.GetPendingAsync(int.MaxValue, ct);
        foreach (var request in pending)
            await _channel.Writer.WriteAsync(request.Id, ct);

        _logger.LogInformation(
            "Booking recovery: {Reset} stuck rows reset, {Enqueued} pending rows re-enqueued",
            reset, pending.Count);
        return pending.Count;
    }

    private async Task PushCreateAsync(OutboundBookingRequest request, CancellationToken ct)
    {
        var draft = JsonSerializer.Deserialize<BookingDraft>(
                request.Payload, OutboundBookingSerialization.DraftJson)
            ?? throw new InvalidOperationException(
                $"Booking request {request.Id}: draft payload deserialized to null.");

        var snapshot = await Retry.ExecuteAsync(
            token => _provider.CreateBookingAsync(draft, token),
            _options.Retry.Attempts,
            TimeSpan.FromSeconds(_options.Retry.BaseDelaySeconds),
            _delayer,
            ct);

        await _ingestService.UpsertFromSnapshotAsync(snapshot, SyncDirection.Outbound, ct);
    }

    private async Task PushCancelAsync(OutboundBookingRequest request, CancellationToken ct)
    {
        var code = request.Payload;

        await Retry.ExecuteAsync(
            async token =>
            {
                await _provider.CancelBookingAsync(code, token);
                return true;
            },
            _options.Retry.Attempts,
            TimeSpan.FromSeconds(_options.Retry.BaseDelaySeconds),
            _delayer,
            ct);

        var booking = await _bookingStore.GetByCodeAsync(code, ct)
            ?? throw new InvalidOperationException(
                $"Booking request {request.Id}: local booking '{code}' vanished before cancel.");
        booking.Cancel(_clock.GetUtcNow());
        await _bookingStore.SaveChangesAsync(ct);
    }

    private async Task NotifyFailureAsync(OutboundBookingRequest request, string error, CancellationToken ct)
    {
        var requestId = request.Id.ToString(CultureInfo.InvariantCulture);
        var notification = new Notification(
            "Outbound booking request failed",
            $"Outbound booking request {requestId} ({request.Kind}) failed after retries and needs human follow-up. Error: {error}",
            NotificationSeverity.Warning,
            new Dictionary<string, string>
            {
                ["requestId"] = requestId,
                ["kind"] = request.Kind.ToString(),
            });
        await _notifications.SendAsync(notification, ct);
    }

    private static string Truncate(string value)
        => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
