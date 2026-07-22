using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Domain.Bookings.Providers;

public sealed record BookingItemSnapshot(
    int ExternalId, int ProductExternalId, int OptionExternalId,
    string ParticipantTypeAlias, DateTimeOffset ActivityAt, decimal FinalPrice);

public sealed record BookingExtraSnapshot(
    int ExternalId, int OptionExternalId, string ExtraAlias, string? Title,
    DateTimeOffset? ActivityAt, int Quantity, decimal FinalPrice);

public sealed record BookingPaymentSnapshot(
    int ExternalId, decimal Amount, string? PaymentMethod,
    PaymentStatus Status, DateTimeOffset? PaidAt);

public sealed record BookingGiftCardSnapshot(string Code, decimal Amount);

/// <summary>Provider-agnostic booking state; never an aggregate. Adapters map
/// vendor payloads here; BookingIngestService merges these into aggregates.</summary>
public sealed record BookingSnapshot
{
    public required string Code { get; init; }
    public string SecretCode { get; init; } = "";
    public BookingType Type { get; init; }
    public BookingStatus Status { get; init; }
    public decimal FinalPrice { get; init; }
    public string? Referrer { get; init; }
    public string? ChannelBookingCode { get; init; }
    public string? LeadContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? ActivityLanguage { get; init; }
    public DateTimeOffset? CreatedAtExternal { get; init; }
    public DateTimeOffset? UpdatedAtExternal { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public IReadOnlyList<BookingItemSnapshot> Items { get; init; } = [];
    public IReadOnlyList<BookingExtraSnapshot> Extras { get; init; } = [];
    public IReadOnlyList<BookingPaymentSnapshot> Payments { get; init; } = [];
    public IReadOnlyList<BookingGiftCardSnapshot> GiftCards { get; init; } = [];
}
