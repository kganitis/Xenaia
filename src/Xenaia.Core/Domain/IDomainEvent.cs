namespace Xenaia.Core.Domain;

/// <summary>Something that happened in the domain, named in past tense.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
