using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>An add-on attached to a booking. Created only through Booking.AddExtra.</summary>
public sealed class BookingExtra : Entity<int>
{
    public int ExternalId { get; private set; }

    public int ExternalOptionId { get; private set; }

    public string ExtraAlias { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public DateTimeOffset? ActivityAt { get; private set; }

    public int Quantity { get; private set; }

    public decimal FinalPrice { get; private set; }

    private BookingExtra(int id) : base(id) { }

    internal BookingExtra(
        int externalId,
        int externalOptionId,
        string extraAlias,
        string? title,
        DateTimeOffset? activityAt,
        int quantity,
        decimal finalPrice) : base(0)
    {
        ExternalId = externalId;
        ExternalOptionId = externalOptionId;
        ExtraAlias = extraAlias;
        Title = title;
        ActivityAt = activityAt;
        Quantity = quantity;
        FinalPrice = finalPrice;
    }

    internal void Amend(string? title, DateTimeOffset? activityAt, int quantity, decimal finalPrice)
    {
        Title = title;
        ActivityAt = activityAt;
        Quantity = quantity;
        FinalPrice = finalPrice;
    }
}
