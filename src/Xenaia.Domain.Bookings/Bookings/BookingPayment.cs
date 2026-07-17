using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>A payment record for a booking. Created only through Booking.RecordPayment.</summary>
public sealed class BookingPayment : Entity<int>
{
    public int ExternalId { get; private set; }

    public decimal Amount { get; private set; }

    public string? PaymentMethod { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    private BookingPayment(int id) : base(id) { }

    internal BookingPayment(
        int externalId,
        decimal amount,
        string? paymentMethod,
        PaymentStatus status,
        DateTimeOffset? paidAt) : base(0)
    {
        ExternalId = externalId;
        Amount = amount;
        PaymentMethod = paymentMethod;
        Status = status;
        PaidAt = paidAt;
    }
}
