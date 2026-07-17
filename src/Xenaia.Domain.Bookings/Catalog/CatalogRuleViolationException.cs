using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Catalog;

/// <summary>Raised when a catalog operation would violate an invariant.</summary>
public sealed class CatalogRuleViolationException(string message) : DomainException(message);
