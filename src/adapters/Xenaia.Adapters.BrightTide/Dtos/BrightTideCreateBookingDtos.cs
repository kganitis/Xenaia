using System.Text.Json.Serialization;

namespace Xenaia.Adapters.BrightTide.Dtos;

/// <summary>Body of POST bookings/start (step one of the three-step create).</summary>
internal sealed class BrightTideStartRequestDto
{
    [JsonPropertyName("booking_type")] public required string BookingType { get; set; }
    [JsonPropertyName("lead_contact_name")] public string? LeadContactName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("activity_language")] public string? ActivityLanguage { get; set; }
}

/// <summary>Response of POST bookings/start: the assigned code and secret.</summary>
internal sealed class BrightTideStartResponseDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("secret_code")] public string? SecretCode { get; set; }
}

/// <summary>Body of POST bookings/items (step two, with the Secret-Code header).</summary>
internal sealed class BrightTideItemsRequestDto
{
    [JsonPropertyName("code")] public required string Code { get; set; }
    [JsonPropertyName("items")] public required List<BrightTideDraftItemDto> Items { get; set; }
}

internal sealed class BrightTideDraftItemDto
{
    [JsonPropertyName("product_id")] public int ProductId { get; set; }
    [JsonPropertyName("product_option_id")] public int ProductOptionId { get; set; }
    [JsonPropertyName("participant_type_alias")] public required string ParticipantTypeAlias { get; set; }
    [JsonPropertyName("activity_date_time")] public required string ActivityDateTime { get; set; }
    [JsonPropertyName("final_price")] public decimal FinalPrice { get; set; }
}

/// <summary>Body of POST bookings/complete (step three).</summary>
internal sealed class BrightTideCompleteRequestDto
{
    [JsonPropertyName("code")] public required string Code { get; set; }
}
