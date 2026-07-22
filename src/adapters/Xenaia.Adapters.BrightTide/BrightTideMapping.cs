using System.Globalization;
using Xenaia.Adapters.BrightTide.Dtos;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Adapters.BrightTide;

/// <summary>
/// Total, throw-free mapping between BrightTide DTOs and the provider-agnostic
/// snapshot records. Unknown enum strings map to the canonical Unknown value;
/// date strings are parsed defensively (the vendor emits non-ISO formats) and
/// an unparsable value yields a null timestamp rather than an exception.
/// </summary>
internal static class BrightTideMapping
{
    /// <summary>Non-ISO formats the vendor is known to emit, tried before the
    /// general parser so day/month order stays unambiguous.</summary>
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "dd-MM-yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "yyyy-MM-dd",
    ];

    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (var format in DateFormats)
        {
            if (DateTimeOffset.TryParseExact(
                value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
                return exact;
        }

        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    public static BookingStatus MapStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pending" => BookingStatus.Pending,
        "completed" => BookingStatus.Completed,
        "cancelled" => BookingStatus.Cancelled,
        "unconfirmed" => BookingStatus.Unconfirmed,
        "deprecated" => BookingStatus.Deprecated,
        _ => BookingStatus.Unknown,
    };

    public static BookingType MapType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "landing" => BookingType.Landing,
        "admin" => BookingType.Admin,
        "api" => BookingType.Api,
        _ => BookingType.Unknown,
    };

    public static PaymentStatus MapPaymentStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pending" => PaymentStatus.Pending,
        "captured" => PaymentStatus.Captured,
        "refunded" => PaymentStatus.Refunded,
        "failed" => PaymentStatus.Failed,
        _ => PaymentStatus.Unknown,
    };

    /// <summary>Canonical enum to the lowercase token BrightTide expects in
    /// query strings and request bodies.</summary>
    public static string ToVendorToken(BookingStatus status) => status.ToString().ToLowerInvariant();

    public static string ToVendorToken(BookingType type) => type.ToString().ToLowerInvariant();

    public static BookingSnapshot ToSnapshot(BrightTideBookingDto dto) => new()
    {
        Code = dto.Code ?? "",
        SecretCode = dto.SecretCode ?? "",
        Type = MapType(dto.BookingType),
        Status = MapStatus(dto.BookingStatus),
        FinalPrice = dto.FinalPrice,
        Referrer = dto.Referrer,
        ChannelBookingCode = dto.ChannelBookingCode,
        LeadContactName = dto.LeadContactName,
        Email = dto.Email,
        Phone = dto.Phone,
        ActivityLanguage = dto.ActivityLanguage,
        CreatedAtExternal = ParseDate(dto.CreatedDateTime),
        UpdatedAtExternal = ParseDate(dto.UpdateDateTime),
        CancelledAt = ParseDate(dto.CancelledDateTime),
        Items = (dto.Items ?? []).Select(i => new BookingItemSnapshot(
            i.Id, i.ProductId, i.ProductOptionId, i.ParticipantTypeAlias ?? "",
            ParseDate(i.ActivityDateTime) ?? default, i.FinalPrice)).ToList(),
        Extras = (dto.Extras ?? []).Select(e => new BookingExtraSnapshot(
            e.Id, e.ProductOptionId, e.ExtraAlias ?? "", e.Title,
            ParseDate(e.ActivityDateTime), e.Quantity, e.FinalPrice)).ToList(),
        Payments = (dto.Payments ?? []).Select(p => new BookingPaymentSnapshot(
            p.Id, p.Amount, p.PaymentMethod, MapPaymentStatus(p.PaymentStatus),
            ParseDate(p.PaidDateTime))).ToList(),
        GiftCards = (dto.GiftCards ?? []).Select(g => new BookingGiftCardSnapshot(
            g.Code ?? "", g.Amount)).ToList(),
    };

    public static ProductSnapshot ToSnapshot(BrightTideProductDto dto) =>
        new(dto.Id, dto.Title ?? "", dto.CategoryId);

    public static ProductOptionSnapshot ToSnapshot(BrightTideProductOptionDto dto) =>
        new(dto.Id, dto.Title ?? "",
            (dto.ParticipantTypes ?? [])
                .Select(p => new ParticipantTypeSnapshot(p.Alias ?? "", p.Title ?? ""))
                .ToList());

    /// <summary>Null when the vendor's timeslot instant is unparsable (skipped
    /// rather than surfaced as a bogus slot).</summary>
    public static AvailabilityTimeslot? ToTimeslot(BrightTideAvailabilityDto dto)
    {
        var at = ParseDate(dto.DateTime);
        return at is null ? null : new AvailabilityTimeslot(at.Value, dto.Vacancies);
    }
}
