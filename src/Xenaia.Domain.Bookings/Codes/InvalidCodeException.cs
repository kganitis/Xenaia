using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>Raised when a code value does not match the tenant's format.</summary>
public sealed class InvalidCodeException(string message) : DomainException(message);
