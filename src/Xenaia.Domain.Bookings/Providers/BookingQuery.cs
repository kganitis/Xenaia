using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Domain.Bookings.Providers;

/// <summary>All filters optional; adapters translate to vendor query syntax.</summary>
public sealed record BookingQuery
{
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedTo { get; init; }
    public DateTimeOffset? BookedFrom { get; init; }
    public DateTimeOffset? BookedTo { get; init; }
    public DateTimeOffset? ActivityFrom { get; init; }
    public DateTimeOffset? ActivityTo { get; init; }
    public BookingStatus? Status { get; init; }
    public BookingType? Type { get; init; }
    public string? Referrer { get; init; }
}
