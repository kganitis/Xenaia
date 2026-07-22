using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Domain.Bookings.Sync;

/// <summary>
/// Merges a provider-agnostic BookingSnapshot into the local Booking
/// aggregate: born on first sight, drifted (scalars, children, status,
/// cancellation) on every subsequent sight, always leaving Sync Synced.
/// </summary>
public sealed class BookingIngestService(
    IBookingStore store, CodeFormats formats, TimeProvider clock)
{
    public async Task<Booking> UpsertFromSnapshotAsync(
        BookingSnapshot snapshot, SyncDirection direction, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await store.GetByCodeAsync(snapshot.Code, ct);
        if (existing is null)
        {
            var code = BookingCode.Create(snapshot.Code, formats.BookingCode);
            var bornStatus = snapshot.Status == BookingStatus.Cancelled
                ? BookingStatus.Pending : snapshot.Status;
            var booking = Booking.Receive(
                code, snapshot.SecretCode, snapshot.Type, bornStatus,
                snapshot.FinalPrice, direction, snapshot.CreatedAtExternal ?? now);
            ApplyScalars(booking, snapshot);
            MergeChildren(booking, snapshot);
            if (snapshot.Status == BookingStatus.Cancelled)
                booking.Cancel(snapshot.CancelledAt ?? now);
            booking.ClaimForSync();
            booking.MarkSynced(now);
            await store.AddAsync(booking, ct);
            await store.SaveChangesAsync(ct);
            return booking;
        }

        ApplyScalars(existing, snapshot);
        if (snapshot.Status == BookingStatus.Cancelled)
        {
            if (existing.Status != BookingStatus.Cancelled)
                existing.Cancel(snapshot.CancelledAt ?? now);
        }
        else
        {
            existing.ApplyStatus(snapshot.Status);
        }
        existing.Reprice(snapshot.FinalPrice);
        MergeChildren(existing, snapshot);
        existing.RequeueSync();
        existing.ClaimForSync();
        existing.MarkSynced(now);
        await store.SaveChangesAsync(ct);
        return existing;
    }

    private static void ApplyScalars(Booking booking, BookingSnapshot snapshot)
    {
        booking.UpdateContact(snapshot.LeadContactName, snapshot.Email, snapshot.Phone);
        booking.SetChannelReference(snapshot.ChannelBookingCode, snapshot.Referrer);
        booking.SetActivityLanguage(snapshot.ActivityLanguage);
        booking.SetExternalTimestamps(snapshot.CreatedAtExternal, snapshot.UpdatedAtExternal);
    }

    private static void MergeChildren(Booking booking, BookingSnapshot snapshot)
    {
        var itemIds = snapshot.Items.Select(i => i.ExternalId).ToHashSet();
        foreach (var item in booking.Items.Where(i => !itemIds.Contains(i.ExternalId)).ToList())
            booking.RemoveItem(item.ExternalId);
        foreach (var item in snapshot.Items)
        {
            if (booking.Items.Any(i => i.ExternalId == item.ExternalId))
                booking.AmendItem(
                    item.ExternalId, item.ParticipantTypeAlias, item.ActivityAt, item.FinalPrice);
            else
                booking.AddItem(
                    item.ExternalId, item.ProductExternalId, item.OptionExternalId,
                    item.ParticipantTypeAlias, item.ActivityAt, item.FinalPrice);
        }

        var extraIds = snapshot.Extras.Select(e => e.ExternalId).ToHashSet();
        foreach (var extra in booking.Extras.Where(e => !extraIds.Contains(e.ExternalId)).ToList())
            booking.RemoveExtra(extra.ExternalId);
        foreach (var extra in snapshot.Extras)
        {
            if (booking.Extras.Any(e => e.ExternalId == extra.ExternalId))
                booking.AmendExtra(
                    extra.ExternalId, extra.Title, extra.ActivityAt, extra.Quantity, extra.FinalPrice);
            else
                booking.AddExtra(
                    extra.ExternalId, extra.OptionExternalId, extra.ExtraAlias,
                    extra.Title, extra.ActivityAt, extra.Quantity, extra.FinalPrice);
        }

        var paymentIds = snapshot.Payments.Select(p => p.ExternalId).ToHashSet();
        foreach (var payment in booking.Payments.Where(p => !paymentIds.Contains(p.ExternalId)).ToList())
            booking.RemovePayment(payment.ExternalId);
        foreach (var payment in snapshot.Payments)
        {
            if (booking.Payments.Any(p => p.ExternalId == payment.ExternalId))
                booking.AmendPayment(
                    payment.ExternalId, payment.Amount, payment.PaymentMethod,
                    payment.Status, payment.PaidAt);
            else
                booking.RecordPayment(
                    payment.ExternalId, payment.Amount, payment.PaymentMethod,
                    payment.Status, payment.PaidAt);
        }

        var giftCardCodes = snapshot.GiftCards.Select(g => g.Code).ToHashSet();
        foreach (var giftCard in booking.GiftCards.Where(g => !giftCardCodes.Contains(g.Code)).ToList())
            booking.RemoveGiftCard(giftCard.Code);
        foreach (var giftCard in snapshot.GiftCards)
        {
            if (booking.GiftCards.Any(g => g.Code == giftCard.Code))
                booking.AmendGiftCard(giftCard.Code, giftCard.Amount);
            else
                booking.ApplyGiftCard(giftCard.Code, giftCard.Amount);
        }
    }
}
