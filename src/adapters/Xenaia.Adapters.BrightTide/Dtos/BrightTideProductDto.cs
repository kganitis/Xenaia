using System.Text.Json.Serialization;

namespace Xenaia.Adapters.BrightTide.Dtos;

/// <summary>Item shape of GET products?content=false.</summary>
internal sealed class BrightTideProductDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("category_id")] public int? CategoryId { get; set; }
}

/// <summary>Item shape of GET products/{pid}/options?content=false.</summary>
internal sealed class BrightTideProductOptionDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("participant_types")] public List<BrightTideParticipantTypeDto>? ParticipantTypes { get; set; }
}

internal sealed class BrightTideParticipantTypeDto
{
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}
