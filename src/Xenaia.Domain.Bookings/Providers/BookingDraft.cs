using Xenaia.Domain.Bookings.Bookings;

namespace Xenaia.Domain.Bookings.Providers;

public sealed record BookingDraftItem(
    int ProductExternalId, int OptionExternalId,
    string ParticipantTypeAlias, DateTimeOffset ActivityAt, decimal FinalPrice);

/// <summary>A locally originated booking before the booking system has
/// accepted it. No code, no secret code: the system assigns both.
/// Construction validates at the edge (fail closed).</summary>
public sealed record BookingDraft
{
    public BookingType Type { get; init; } = BookingType.Api;
    public string? LeadContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? ActivityLanguage { get; init; }
    public string? Referrer { get; init; }
    public required IReadOnlyList<BookingDraftItem> Items { get; init; }

    /// <summary>Throws ArgumentException when structurally invalid:
    /// no items, a non-positive external id, a blank participant alias,
    /// or a negative price.</summary>
    public void EnsureValid()
    {
        if (Items is not { Count: > 0 })
            throw new ArgumentException("A booking draft needs at least one item.");
        foreach (var item in Items)
        {
            if (item.ProductExternalId <= 0 || item.OptionExternalId <= 0)
                throw new ArgumentException("Draft item external ids must be positive.");
            if (string.IsNullOrWhiteSpace(item.ParticipantTypeAlias))
                throw new ArgumentException("Draft item participant type alias is blank.");
            if (item.FinalPrice < 0)
                throw new ArgumentException("Draft item price cannot be negative.");
        }
    }
}
