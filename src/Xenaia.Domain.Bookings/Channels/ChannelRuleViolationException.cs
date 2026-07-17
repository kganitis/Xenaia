using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Channels;

/// <summary>Raised when a channel operation would violate an invariant.</summary>
public sealed class ChannelRuleViolationException(string message) : DomainException(message);
