using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>Raised when a booking operation would violate an aggregate invariant.</summary>
public sealed class BookingRuleViolationException(string message) : DomainException(message);
