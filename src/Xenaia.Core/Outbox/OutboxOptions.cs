using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Core.Outbox;

/// <summary>Drainer tuning. Section is required (fail closed) even though
/// every property has a safe default: absent infrastructure config should
/// be a deliberate choice, not an accident.</summary>
public sealed record OutboxOptions : ISectionOptions
{
    public static string SectionName => "Outbox";

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 10;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;
}
