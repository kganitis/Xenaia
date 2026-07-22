namespace Xenaia.Domain.Bookings.Stores;

/// <summary>
/// Thrown by an availability store when a save fails because another writer
/// inserted a row with the same (product, option, timeslot) key first: a
/// unique-index insert race (Postgres <c>23505</c>). The patch service catches
/// this to retry the conflicting adds as merges onto the now-existing rows.
/// </summary>
public sealed class DuplicateAvailabilityException(
    IReadOnlyList<AvailabilityKey> conflictingKeys, Exception? innerException = null)
    : Exception("An availability row with the same key already exists.", innerException)
{
    /// <summary>The keys of the rows that could not be inserted, when the store
    /// can identify them cheaply; empty otherwise.</summary>
    public IReadOnlyList<AvailabilityKey> ConflictingKeys { get; } = conflictingKeys;
}
