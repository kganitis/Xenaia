using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xenaia.Adapters.Freshdesk;

internal sealed class FreshdeskTicketDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("custom_fields")] public Dictionary<string, JsonElement>? CustomFields { get; set; }
    [JsonPropertyName("requester")] public FreshdeskRequesterDto? Requester { get; set; }
}

internal sealed class FreshdeskRequesterDto
{
    [JsonPropertyName("email")] public string? Email { get; set; }
}
