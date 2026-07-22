using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Sync.Bookings;

/// <summary>
/// Turns a locally originated create/cancel into a durable
/// <see cref="OutboundBookingRequest"/> and wakes the pusher (spec 6.4 step 1).
/// Validation happens before anything is persisted, so a rejected request
/// leaves no row behind. Task 16's <c>POST /api/bookings</c> and
/// <c>POST /api/bookings/{code}/cancel</c> endpoints are the callers.
/// </summary>
public sealed class OutboundBookingEnqueuer(
    IOutboundBookingRequestStore store,
    IBookingStore bookingStore,
    ICatalogStore catalog,
    BookingChannel channel,
    ILogger<OutboundBookingEnqueuer> logger)
{
    /// <summary>Validates the draft (structure, then catalog existence of each
    /// item's product/option), persists a Create request as Pending, wakes the
    /// channel, and returns the request id. Throws on a structurally invalid
    /// draft (ArgumentException) or an unknown product/option
    /// (InvalidOperationException); nothing is persisted on failure.</summary>
    public async Task<int> EnqueueCreateAsync(BookingDraft draft, CancellationToken ct)
    {
        draft.EnsureValid();
        await ValidateAgainstCatalogAsync(draft, ct);

        var json = JsonSerializer.Serialize(draft, OutboundBookingSerialization.DraftJson);
        var request = OutboundBookingRequest.ForCreate(json);
        await store.AddAsync(request, ct);
        await store.SaveChangesAsync(ct);

        await channel.Writer.WriteAsync(request.Id, ct);
        logger.LogInformation("Outbound booking create request {Id} enqueued", request.Id);
        return request.Id;
    }

    /// <summary>Verifies the local booking exists and is not already cancelled,
    /// persists a Cancel request as Pending, wakes the channel, and returns the
    /// request id. Throws <see cref="InvalidOperationException"/> when the code
    /// is unknown or the booking is already cancelled (the endpoint maps those
    /// to 404/409).</summary>
    public async Task<int> EnqueueCancelAsync(string bookingCode, CancellationToken ct)
    {
        var booking = await bookingStore.GetByCodeAsync(bookingCode, ct)
            ?? throw new InvalidOperationException($"No local booking with code '{bookingCode}'.");
        if (booking.Status == BookingStatus.Cancelled)
            throw new InvalidOperationException($"Booking '{bookingCode}' is already cancelled.");

        var request = OutboundBookingRequest.ForCancel(bookingCode);
        await store.AddAsync(request, ct);
        await store.SaveChangesAsync(ct);

        await channel.Writer.WriteAsync(request.Id, ct);
        logger.LogInformation("Outbound booking cancel request {Id} enqueued for {Code}", request.Id, bookingCode);
        return request.Id;
    }

    private async Task ValidateAgainstCatalogAsync(BookingDraft draft, CancellationToken ct)
    {
        var products = await catalog.GetProductsAsync(ct);
        var byExternalId = products.ToDictionary(p => p.ExternalId);

        foreach (var item in draft.Items)
        {
            if (!byExternalId.TryGetValue(item.ProductExternalId, out var product))
                throw new InvalidOperationException(
                    $"Unknown product external id {item.ProductExternalId} in booking draft.");
            if (product.Options.All(o => o.ExternalId != item.OptionExternalId))
                throw new InvalidOperationException(
                    $"Unknown option external id {item.OptionExternalId} for product {item.ProductExternalId}.");
        }
    }
}
