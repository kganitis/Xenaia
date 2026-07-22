using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>A gift card applied to a booking. Created only through Booking.ApplyGiftCard.</summary>
public sealed class BookingGiftCard : Entity<int>
{
    public string Code { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    private BookingGiftCard(int id) : base(id) { }

    internal BookingGiftCard(string code, decimal amount) : base(0)
    {
        Code = code;
        Amount = amount;
    }

    internal void Amend(decimal amount) => Amount = amount;
}
