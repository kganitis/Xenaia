using System.Text.Json.Serialization;

namespace Xenaia.Adapters.BrightTide.Dtos;

/// <summary>Item shape of GET products/{pid}/options/{oid}/availability.</summary>
internal sealed class BrightTideAvailabilityDto
{
    [JsonPropertyName("date_time")] public string? DateTime { get; set; }
    [JsonPropertyName("vacancies")] public int Vacancies { get; set; }
}

/// <summary>Body of PATCH products/availability.</summary>
internal sealed class BrightTideAvailabilityPatchDto
{
    [JsonPropertyName("updates")] public required List<BrightTideAvailabilityUpdateDto> Updates { get; set; }
}

internal sealed class BrightTideAvailabilityUpdateDto
{
    [JsonPropertyName("product_id")] public int ProductId { get; set; }
    [JsonPropertyName("product_option_id")] public int ProductOptionId { get; set; }
    [JsonPropertyName("from")] public required string From { get; set; }
    [JsonPropertyName("to")] public required string To { get; set; }
    [JsonPropertyName("times")] public List<string>? Times { get; set; }
    [JsonPropertyName("vacancies")] public int? Vacancies { get; set; }
    [JsonPropertyName("stop_sales")] public bool? StopSales { get; set; }
    [JsonPropertyName("participant_types")] public required List<string> ParticipantTypes { get; set; }
}
