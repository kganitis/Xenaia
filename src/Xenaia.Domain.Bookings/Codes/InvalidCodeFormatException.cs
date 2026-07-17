using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>Raised when a tenant-configured code pattern cannot be compiled.</summary>
public sealed class InvalidCodeFormatException(string message) : DomainException(message);
