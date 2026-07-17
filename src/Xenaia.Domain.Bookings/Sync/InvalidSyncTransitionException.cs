using Xenaia.Core.Domain;

namespace Xenaia.Domain.Bookings.Sync;

/// <summary>Raised when a sync transition is not in the state machine's table.</summary>
public sealed class InvalidSyncTransitionException(SyncStatus from, SyncStatus to)
    : DomainException($"Sync transition {from} -> {to} is not allowed.")
{
    public SyncStatus From { get; } = from;

    public SyncStatus To { get; } = to;
}
