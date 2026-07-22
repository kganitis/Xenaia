using System.Net;
using System.Text;
using System.Text.Json;
using Xenaia.Adapters.BrightTide;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;
using Xunit;

namespace Xenaia.Adapters.BrightTide.Tests;

/// <summary>
/// Wire-level guarantees for the client over a canned-response stub handler:
/// the static API-Key header, exact query-string building, the vendor's
/// null/empty conventions, the three-step create composition, and the error
/// wrapping contract.
/// </summary>
public class BrightTideClientTests
{
    private const string ApiKey = "test-key";

    /// <summary>Canned-response handler recording every request and body.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) =>
            _responses.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    /// <summary>Handler that always throws, to exercise transport-fault wrapping.</summary>
    private sealed class ThrowingHandler(Exception toThrow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => throw toThrow;
    }

    private static (BrightTideClient Client, StubHandler Handler) Build()
    {
        var handler = new StubHandler();
        var client = ClientOver(handler);
        return (client, handler);
    }

    private static BrightTideClient ClientOver(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://brighttide.example/") };
        http.DefaultRequestHeaders.Add("API-Key", ApiKey);
        return new BrightTideClient(http);
    }

    [Fact]
    public async Task Api_key_header_is_present_on_every_request()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetProductsAsync(CancellationToken.None);
        await client.GetBookingsAsync(new BookingQuery(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.True(r.Headers.TryGetValues("API-Key", out var values));
            Assert.Equal(ApiKey, Assert.Single(values!));
        });
    }

    [Fact]
    public async Task Booking_query_builds_the_exact_filter_string()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetBookingsAsync(new BookingQuery
        {
            Status = BookingStatus.Completed,
            UpdatedFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedTo = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero),
        }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/bookings?booking_status=completed&update_date_time_from=2026-07-01&update_date_time_to=2026-07-31",
            request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Get_booking_by_code_returns_null_on_404()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        var booking = await client.GetBookingByCodeAsync("MT-MISSING", CancellationToken.None);

        Assert.Null(booking);
        Assert.EndsWith("bookings/MT-MISSING", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Cancel_maps_404_to_entity_not_found()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        await Assert.ThrowsAsync<BookingSystemEntityNotFoundException>(() =>
            client.CancelBookingAsync("MT-MISSING", CancellationToken.None));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("bookings/MT-MISSING/cancel", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Availability_returns_null_on_404_and_empty_on_400()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        handler.Enqueue(HttpStatusCode.BadRequest, "{}");

        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(await client.GetAvailabilityAsync(1, 2, from, to, CancellationToken.None));

        var empty = await client.GetAvailabilityAsync(1, 2, from, to, CancellationToken.None);
        Assert.NotNull(empty);
        Assert.Empty(empty!);

        Assert.Contains(
            "products/1/options/2/availability?from=2026-08-01T00:00:00&to=2026-08-31T00:00:00&pricing=false",
            handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Create_composes_start_then_items_then_complete_in_order()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{ "code": "MT-NEW1", "secret_code": "SEC-NEW1" }""");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK,
            """{ "code": "MT-NEW1", "secret_code": "SEC-NEW1", "booking_status": "pending", "final_price": 42.0 }""");

        var created = await client.CreateBookingAsync(new BookingDraft
        {
            Type = BookingType.Api,
            Email = "guest@example.net",
            Items = [new BookingDraftItem(100, 200, "adult",
                new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), 42m)],
        }, CancellationToken.None);

        Assert.Equal("MT-NEW1", created.Code);
        Assert.Equal(3, handler.Requests.Count);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("bookings/start", handler.Requests[0].RequestUri!.AbsolutePath);

        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("bookings/items", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.True(handler.Requests[1].Headers.TryGetValues("Secret-Code", out var secret));
        Assert.Equal("SEC-NEW1", Assert.Single(secret!));

        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.EndsWith("bookings/complete", handler.Requests[2].RequestUri!.AbsolutePath);

        using var startBody = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("api", startBody.RootElement.GetProperty("booking_type").GetString());
        using var itemsBody = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal("MT-NEW1", itemsBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(100, itemsBody.RootElement.GetProperty("items")[0].GetProperty("product_id").GetInt32());
    }

    [Fact]
    public async Task Non_success_wraps_to_api_exception_with_status_and_truncated_body()
    {
        var (client, handler) = Build();
        var longBody = new string('x', 900);
        handler.Enqueue(HttpStatusCode.InternalServerError, longBody);

        var ex = await Assert.ThrowsAsync<BrightTideApiException>(() =>
            client.GetProductsAsync(CancellationToken.None));

        Assert.Equal(500, ex.StatusCode);
        Assert.True(ex.ResponseBody.Length < longBody.Length);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Transport_fault_wraps_to_booking_system_exception()
    {
        var client = ClientOver(new ThrowingHandler(new HttpRequestException("connection refused")));

        var ex = await Assert.ThrowsAsync<BookingSystemException>(() =>
            client.GetProductsAsync(CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_wraps_to_booking_system_exception()
    {
        var client = ClientOver(new ThrowingHandler(new TaskCanceledException("timed out")));

        // The caller did not cancel, so a TaskCanceledException is a timeout.
        await Assert.ThrowsAsync<BookingSystemException>(() =>
            client.GetProductsAsync(CancellationToken.None));
    }
}
