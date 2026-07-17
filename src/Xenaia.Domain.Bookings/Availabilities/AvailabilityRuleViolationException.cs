using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Availabilities;

/// <summary>Raised when an availability operation would violate an invariant.</summary>
public sealed class AvailabilityRuleViolationException(string message) : DomainException(message);
