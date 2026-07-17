using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings.Events;

public sealed record BookingSyncFailed(
    string Code,
    string Error,
    DateTimeOffset OccurredAt) : IDomainEvent;
