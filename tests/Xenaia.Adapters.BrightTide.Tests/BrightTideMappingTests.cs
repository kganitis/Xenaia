using System.Text.Json;
using Xenaia.Adapters.BrightTide;
using Xenaia.Adapters.BrightTide.Dtos;
using Xenaia.Domain.Bookings.Bookings;
using Xunit;

namespace Xenaia.Adapters.BrightTide.Tests;

/// <summary>
/// Mapping-level guarantees: snake_case DTOs round-trip, enum strings map
/// tolerantly (unknown -> Unknown), and the vendor's non-ISO date strings are
/// parsed defensively (unparsable -> null, never throw).
/// </summary>
public class BrightTideMappingTests
{
    private const string BookingJson = """
        {
          "code": "MT-7Q2K9F4A",
          "secret_code": "SEC-7Q2K9F4A",
          "booking_type": "api",
          "booking_status": "pending",
          "final_price": 84.50,
          "referrer": "wavehopper",
          "channel_booking_code": "WH-123",
          "lead_contact_name": "Guest",
          "email": "guest@example.net",
          "phone": "+30 210 0000000",
          "activity_language": "en",
          "created_date_time": "2026-07-01T09:00:00",
          "update_date_time": "2026-07-02T11:30:00",
          "cancelled_date_time": null,
          "items": [
            { "id": 1, "product_id": 100, "product_option_id": 200,
              "participant_type_alias": "adult", "activity_date_time": "2026-08-01T09:00:00", "final_price": 42.25 }
          ],
          "payments": [
            { "id": 5, "amount": 84.50, "payment_method": "card", "payment_status": "captured", "paid_date_time": "2026-07-01T09:05:00" }
          ],
          "gift_cards": [ { "code": "GC-1", "amount": 10.00 } ]
        }
        """;

    [Fact]
    public void Snake_case_booking_round_trips_into_the_snapshot()
    {
        var dto = JsonSerializer.Deserialize<BrightTideBookingDto>(BookingJson, BrightTideClient.Json)!;

        var snapshot = BrightTideMapping.ToSnapshot(dto);

        Assert.Equal("MT-7Q2K9F4A", snapshot.Code);
        Assert.Equal("SEC-7Q2K9F4A", snapshot.SecretCode);
        Assert.Equal(BookingType.Api, snapshot.Type);
        Assert.Equal(BookingStatus.Pending, snapshot.Status);
        Assert.Equal(84.50m, snapshot.FinalPrice);
        Assert.Equal("wavehopper", snapshot.Referrer);
        Assert.Equal("WH-123", snapshot.ChannelBookingCode);
        Assert.Equal("guest@example.net", snapshot.Email);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), snapshot.CreatedAtExternal);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 11, 30, 0, TimeSpan.Zero), snapshot.UpdatedAtExternal);
        Assert.Null(snapshot.CancelledAt);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal(1, item.ExternalId);
        Assert.Equal(100, item.ProductExternalId);
        Assert.Equal(200, item.OptionExternalId);
        Assert.Equal("adult", item.ParticipantTypeAlias);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), item.ActivityAt);

        var payment = Assert.Single(snapshot.Payments);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        var giftCard = Assert.Single(snapshot.GiftCards);
        Assert.Equal("GC-1", giftCard.Code);
        Assert.Equal(10.00m, giftCard.Amount);
    }

    [Theory]
    [InlineData("pending", BookingStatus.Pending)]
    [InlineData("completed", BookingStatus.Completed)]
    [InlineData("cancelled", BookingStatus.Cancelled)]
    [InlineData("unconfirmed", BookingStatus.Unconfirmed)]
    [InlineData("deprecated", BookingStatus.Deprecated)]
    [InlineData("something_new", BookingStatus.Unknown)]
    [InlineData(null, BookingStatus.Unknown)]
    public void Unknown_booking_status_maps_to_unknown(string? vendor, BookingStatus expected) =>
        Assert.Equal(expected, BrightTideMapping.MapStatus(vendor));

    [Theory]
    [InlineData("landing", BookingType.Landing)]
    [InlineData("api", BookingType.Api)]
    [InlineData("mystery", BookingType.Unknown)]
    public void Unknown_booking_type_maps_to_unknown(string? vendor, BookingType expected) =>
        Assert.Equal(expected, BrightTideMapping.MapType(vendor));

    [Fact]
    public void Non_iso_date_is_parsed_via_a_documented_format()
    {
        var parsed = BrightTideMapping.ParseDate("01-08-2026 14:30:00");

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-13-45")]
    [InlineData("")]
    [InlineData(null)]
    public void Unparsable_date_yields_null_and_never_throws(string? value) =>
        Assert.Null(BrightTideMapping.ParseDate(value));

    [Fact]
    public void Booking_with_an_unparsable_update_date_maps_to_a_null_timestamp()
    {
        var dto = JsonSerializer.Deserialize<BrightTideBookingDto>("""
            { "code": "MT-1", "booking_status": "pending", "update_date_time": "garbage" }
            """, BrightTideClient.Json)!;

        var snapshot = BrightTideMapping.ToSnapshot(dto);

        Assert.Null(snapshot.UpdatedAtExternal);
        Assert.Equal(BookingStatus.Pending, snapshot.Status);
    }

    [Fact]
    public void Request_dto_serializes_with_snake_case_keys()
    {
        var json = JsonSerializer.Serialize(new BrightTideStartRequestDto
        {
            BookingType = "api",
            LeadContactName = "Guest",
            Email = "guest@example.net",
        }, BrightTideClient.Json);

        Assert.Contains("\"booking_type\":\"api\"", json);
        Assert.Contains("\"lead_contact_name\":\"Guest\"", json);
        // WhenWritingNull drops the optional fields we did not set.
        Assert.DoesNotContain("phone", json);
    }
}
