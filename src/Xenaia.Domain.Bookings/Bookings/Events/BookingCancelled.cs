using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings.Events;

public sealed record BookingCancelled(
    string Code,
    DateTimeOffset CancelledAt,
    DateTimeOffset OccurredAt) : IDomainEvent;
