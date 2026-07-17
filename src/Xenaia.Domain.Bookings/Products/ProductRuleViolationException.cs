using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Products;

/// <summary>Raised when a product operation would violate an aggregate invariant.</summary>
public sealed class ProductRuleViolationException(string message) : DomainException(message);
