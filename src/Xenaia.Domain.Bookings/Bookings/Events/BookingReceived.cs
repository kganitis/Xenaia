using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings.Events;

/// <summary>
/// A booking entered the platform. Carries the natural key (code), not the
/// database id: events are drained to the outbox before ids are assigned.
/// </summary>
public sealed record BookingReceived(string Code, DateTimeOffset OccurredAt) : IDomainEvent;
