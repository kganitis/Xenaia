using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xenaia.Adapters.Freshdesk;
using Xenaia.Modules.Triage.Helpdesk;
using Xunit;

namespace Xenaia.Adapters.Freshdesk.Tests;

public class FreshdeskHelpdeskProviderTests
{
    /// <summary>Canned-response handler recording every request.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string json) =>
            _responses.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
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

    private static readonly FreshdeskOptions Options = new()
    {
        BaseUrl = "https://meridian.example/api/v2/",
        ApiKey = "test-key",
        PageSize = 2,
        FieldMap = new Dictionary<string, string>
        {
            ["bookingCode"] = "cf_booking_code",
            ["channel"] = "cf_channel",
        },
    };

    private static (FreshdeskHelpdeskProvider Provider, StubHandler Handler) Build()
    {
        var handler = new StubHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes("test-key:X"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        var provider = new FreshdeskHelpdeskProvider(
            http, Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<FreshdeskHelpdeskProvider>.Instance);
        return (provider, handler);
    }

    private static string TicketJson(long id, string createdAt = "2026-07-01T12:00:00Z") => $$"""
        {
          "id": {{id}},
          "subject": "New Booking [MT-7Q2K9F4A]",
          "description": "<p>Product Code MTP-KAYA</p>",
          "status": 2,
          "priority": 1,
          "created_at": "{{createdAt}}",
          "tags": ["existing"],
          "custom_fields": { "cf_booking_code": "MT-7Q2K9F4A", "cf_channel": "Wavehopper", "cf_internal": "hidden", "cf_flag": true },
          "requester": { "email": "guest@example.net" }
        }
        """;

    [Fact]
    public async Task Open_tickets_map_to_the_port_shape()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, $"[{TicketJson(101)}]");

        var tickets = await provider.GetOpenTicketsAsync(CancellationToken.None);

        var ticket = Assert.Single(tickets);
        Assert.Equal("101", ticket.Id);
        Assert.Equal("New Booking [MT-7Q2K9F4A]", ticket.Subject);
        Assert.Equal("<p>Product Code MTP-KAYA</p>", ticket.BodyHtml);
        Assert.Equal("guest@example.net", ticket.Sender);
        Assert.Equal("Wavehopper", ticket.Channel);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(["existing"], ticket.Tags.ToArray());
        Assert.Equal("MT-7Q2K9F4A", ticket.CustomFields["bookingCode"]);
        Assert.False(ticket.CustomFields.ContainsKey("cf_internal"));
    }

    [Fact]
    public async Task Auth_header_and_list_query_are_correct()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await provider.GetOpenTicketsAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("test-key:X")),
            request.Headers.Authorization.Parameter);
        var query = request.RequestUri!.Query;
        Assert.DoesNotContain("status=", query);
        Assert.Contains("order_by=created_at", query);
        Assert.Contains("order_type=asc", query);
        Assert.Contains("include=description,requester", query);
        Assert.Contains("per_page=2", query);
        Assert.Contains("page=1", query);
    }

    [Fact]
    public async Task Non_open_tickets_in_the_list_response_are_filtered_out()
    {
        var (provider, handler) = Build();
        var closedTicketJson = $$"""
            {
              "id": 202,
              "subject": "Old Booking [MT-9K3L2P7B]",
              "description": "<p>Product Code MTP-KAYA</p>",
              "status": 5,
              "priority": 1,
              "created_at": "2026-07-01T12:00:00Z",
              "tags": ["existing"],
              "custom_fields": { "cf_booking_code": "MT-9K3L2P7B", "cf_channel": "Wavehopper" },
              "requester": { "email": "guest@example.net" }
            }
            """;
        handler.Enqueue(HttpStatusCode.OK, $"[{TicketJson(101)},{closedTicketJson}]");
        handler.Enqueue(HttpStatusCode.OK, "[]");

        var tickets = await provider.GetOpenTicketsAsync(CancellationToken.None);

        var ticket = Assert.Single(tickets);
        Assert.Equal("101", ticket.Id);
        Assert.Equal(TicketStatus.Open, ticket.Status);
    }

    [Fact]
    public async Task Pagination_stops_after_a_short_page()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, $"[{TicketJson(1)},{TicketJson(2)}]");
        handler.Enqueue(HttpStatusCode.OK, $"[{TicketJson(3)}]");

        var tickets = await provider.GetOpenTicketsAsync(CancellationToken.None);

        Assert.Equal(3, tickets.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Update_maps_status_priority_and_custom_fields_to_vendor_values()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, TicketJson(101));

        await provider.UpdateTicketAsync("101", new TicketUpdate
        {
            Status = TicketStatus.Closed,
            Priority = TicketPriority.Urgent,
            SetCustomFields = new Dictionary<string, string> { ["bookingCode"] = "MT-2B8D0E6F" },
        }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("tickets/101", request.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(5, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(4, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("MT-2B8D0E6F",
            body.RootElement.GetProperty("custom_fields").GetProperty("cf_booking_code").GetString());
        Assert.False(body.RootElement.TryGetProperty("tags", out _));
    }

    [Fact]
    public async Task Adding_tags_reads_current_tags_and_sends_the_union()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, TicketJson(101));   // GET tickets/101
        handler.Enqueue(HttpStatusCode.OK, TicketJson(101));   // PUT tickets/101

        await provider.UpdateTicketAsync("101", new TicketUpdate
        {
            AddTags = ["existing", "xenaia-triaged"],
        }, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        using var body = JsonDocument.Parse(handler.RequestBodies[1]);
        var tags = body.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();
        Assert.Equal(["existing", "xenaia-triaged"], tags);
    }

    [Fact]
    public async Task Unmapped_canonical_field_fails_loudly()
    {
        var (provider, _) = Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UpdateTicketAsync("101", new TicketUpdate
            {
                SetCustomFields = new Dictionary<string, string> { ["mystery"] = "x" },
            }, CancellationToken.None));

        Assert.Contains("mystery", ex.Message);
        Assert.Contains("FieldMap", ex.Message);
    }

    [Fact]
    public async Task Vendor_404_becomes_ticket_not_found()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        await Assert.ThrowsAsync<HelpdeskTicketNotFoundException>(() =>
            provider.UpdateTicketAsync("999", new TicketUpdate
            {
                Status = TicketStatus.Closed,
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Private_note_posts_to_the_notes_endpoint()
    {
        var (provider, handler) = Build();
        handler.Enqueue(HttpStatusCode.Created, "{}");

        await provider.AddPrivateNoteAsync("101", "note body", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("tickets/101/notes", request.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("note body", body.RootElement.GetProperty("body").GetString());
        Assert.True(body.RootElement.GetProperty("private").GetBoolean());
    }
}
