using System.Text.Json;
using Xenaia.Core.Domain;

namespace Xenaia.Core.Outbox;

/// <summary>
/// A domain event persisted alongside the state change that raised it,
/// so notifications survive process death (transactional outbox).
/// Record type: tests and tooling may derive variants with `with`.
/// </summary>
public sealed record OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? Error { get; init; }

    public static OutboxMessage From(IDomainEvent domainEvent) => new()
    {
        Type = domainEvent.GetType().AssemblyQualifiedName
               ?? domainEvent.GetType().FullName
               ?? domainEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAt = domainEvent.OccurredAt,
    };

    /// <summary>
    /// Null when the type cannot be resolved, the payload does not parse,
    /// or the parsed object is not a domain event; the drainer marks such
    /// messages failed rather than crashing.
    /// </summary>
    public IDomainEvent? ToDomainEvent()
    {
        var type = System.Type.GetType(Type, throwOnError: false);
        if (type is null) return null;

        try
        {
            return JsonSerializer.Deserialize(Payload, type) as IDomainEvent;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
