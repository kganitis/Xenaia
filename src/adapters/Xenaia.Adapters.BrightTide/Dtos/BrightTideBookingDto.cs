using System.Text.Json.Serialization;

namespace Xenaia.Adapters.BrightTide.Dtos;

/// <summary>
/// The full booking payload BrightTide returns from GET bookings, GET
/// bookings/{code}, and POST bookings/complete. Date fields are carried as
/// strings and parsed defensively by <see cref="BrightTideMapping"/> (the
/// vendor emits non-ISO formats); enum-like fields are lowercase strings.
/// </summary>
internal sealed class BrightTideBookingDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("secret_code")] public string? SecretCode { get; set; }
    [JsonPropertyName("booking_type")] public string? BookingType { get; set; }
    [JsonPropertyName("booking_status")] public string? BookingStatus { get; set; }
    [JsonPropertyName("final_price")] public decimal FinalPrice { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("channel_booking_code")] public string? ChannelBookingCode { get; set; }
    [JsonPropertyName("lead_contact_name")] public string? LeadContactName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("activity_language")] public string? ActivityLanguage { get; set; }
    [JsonPropertyName("created_date_time")] public string? CreatedDateTime { get; set; }
    [JsonPropertyName("update_date_time")] public string? UpdateDateTime { get; set; }
    [JsonPropertyName("cancelled_date_time")] public string? CancelledDateTime { get; set; }
    [JsonPropertyName("items")] public List<BrightTideBookingItemDto>? Items { get; set; }
    [JsonPropertyName("extras")] public List<BrightTideBookingExtraDto>? Extras { get; set; }
    [JsonPropertyName("payments")] public List<BrightTideBookingPaymentDto>? Payments { get; set; }
    [JsonPropertyName("gift_cards")] public List<BrightTideGiftCardDto>? GiftCards { get; set; }
}

internal sealed class BrightTideBookingItemDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("product_id")] public int ProductId { get; set; }
    [JsonPropertyName("product_option_id")] public int ProductOptionId { get; set; }
    [JsonPropertyName("participant_type_alias")] public string? ParticipantTypeAlias { get; set; }
    [JsonPropertyName("activity_date_time")] public string? ActivityDateTime { get; set; }
    [JsonPropertyName("final_price")] public decimal FinalPrice { get; set; }
}

internal sealed class BrightTideBookingExtraDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("product_option_id")] public int ProductOptionId { get; set; }
    [JsonPropertyName("extra_alias")] public string? ExtraAlias { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("activity_date_time")] public string? ActivityDateTime { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("final_price")] public decimal FinalPrice { get; set; }
}

internal sealed class BrightTideBookingPaymentDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("payment_method")] public string? PaymentMethod { get; set; }
    [JsonPropertyName("payment_status")] public string? PaymentStatus { get; set; }
    [JsonPropertyName("paid_date_time")] public string? PaidDateTime { get; set; }
}

internal sealed class BrightTideGiftCardDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
}
