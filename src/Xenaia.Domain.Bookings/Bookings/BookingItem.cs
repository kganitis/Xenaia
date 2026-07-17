using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>
/// A participant line in a booking. Created only through Booking.AddItem;
/// no sync state of its own (it rides on its root).
/// </summary>
public sealed class BookingItem : Entity<int>
{
    public int ExternalId { get; private set; }

    public int ExternalProductId { get; private set; }

    public int ExternalOptionId { get; private set; }

    public string ParticipantTypeAlias { get; private set; } = string.Empty;

    public DateTimeOffset ActivityAt { get; private set; }

    public decimal FinalPrice { get; private set; }

    public bool CheckedIn { get; private set; }

    private BookingItem(int id) : base(id) { }

    internal BookingItem(
        int externalId,
        int externalProductId,
        int externalOptionId,
        string participantTypeAlias,
        DateTimeOffset activityAt,
        decimal finalPrice) : base(0)
    {
        ExternalId = externalId;
        ExternalProductId = externalProductId;
        ExternalOptionId = externalOptionId;
        ParticipantTypeAlias = participantTypeAlias;
        ActivityAt = activityAt;
        FinalPrice = finalPrice;
    }

    internal void CheckIn() => CheckedIn = true;
}
