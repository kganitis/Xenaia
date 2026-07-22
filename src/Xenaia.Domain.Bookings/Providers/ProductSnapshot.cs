namespace Xenaia.Domain.Bookings.Providers;

public sealed record ProductSnapshot(int ExternalId, string Title, int? CategoryExternalId);

public sealed record ParticipantTypeSnapshot(string Alias, string Title);

public sealed record ProductOptionSnapshot(
    int ExternalId, string Title,
    IReadOnlyList<ParticipantTypeSnapshot> ParticipantTypes);
