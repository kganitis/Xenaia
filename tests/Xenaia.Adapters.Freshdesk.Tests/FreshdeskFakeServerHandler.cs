using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Adapters.Freshdesk.Tests;

/// <summary>
/// A stateful fake of the Freshdesk v2 endpoints the adapter uses: ticket
/// listing with pagination, single-ticket GET, ticket PUT, and note POST.
/// State is held as port-shaped tickets; JSON is rendered vendor-shaped on
/// the way out and merged vendor-shaped on the way in.
/// </summary>
public sealed partial class FreshdeskFakeServerHandler(
    IReadOnlyList<HelpdeskTicket> seed,
    IReadOnlyDictionary<string, string> fieldMap) : HttpMessageHandler
{
    private readonly Dictionary<string, HelpdeskTicket> _tickets =
        seed.ToDictionary(t => t.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _notes =
        seed.ToDictionary(t => t.Id, _ => new List<string>(), StringComparer.Ordinal);

    public HelpdeskTicket Snapshot(string id) => _tickets[id];

    public IReadOnlyList<string> Notes(string id) => _notes[id];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

        if (request.Method == HttpMethod.Get && path.EndsWith("/tickets", StringComparison.Ordinal))
            return ListTickets(request.RequestUri);

        var noteMatch = NotePath().Match(path);
        if (request.Method == HttpMethod.Post && noteMatch.Success)
            return AddNote(noteMatch.Groups[1].Value, body);

        var ticketMatch = TicketPath().Match(path);
        if (ticketMatch.Success)
        {
            var id = ticketMatch.Groups[1].Value;
            if (!_tickets.ContainsKey(id))
                return Json(HttpStatusCode.NotFound, "{}");
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, Render(_tickets[id]));
            if (request.Method == HttpMethod.Put)
                return UpdateTicket(id, body);
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private HttpResponseMessage ListTickets(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        var page = int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture);
        var perPage = int.Parse(query["per_page"] ?? "30", CultureInfo.InvariantCulture);

        var open = _tickets.Values
            .Where(t => t.Status == TicketStatus.Open)
            .OrderBy(t => t.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(Render);
        return Json(HttpStatusCode.OK, "[" + string.Join(",", open) + "]");
    }

    private HttpResponseMessage AddNote(string id, string body)
    {
        if (!_notes.TryGetValue(id, out var notes))
            return Json(HttpStatusCode.NotFound, "{}");
        using var doc = JsonDocument.Parse(body);
        notes.Add(doc.RootElement.GetProperty("body").GetString() ?? "");
        return Json(HttpStatusCode.Created, "{}");
    }

    private HttpResponseMessage UpdateTicket(string id, string body)
    {
        var ticket = _tickets[id];
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status))
            ticket = ticket with { Status = VendorStatus(status.GetInt32()) };
        if (root.TryGetProperty("priority", out var priority))
            ticket = ticket with { Priority = (TicketPriority)(priority.GetInt32() - 1) };
        if (root.TryGetProperty("tags", out var tags))
            ticket = ticket with
            {
                Tags = tags.EnumerateArray().Select(t => t.GetString() ?? "").ToList(),
            };
        if (root.TryGetProperty("custom_fields", out var vendorFields))
        {
            var canonicalByVendor = fieldMap.ToDictionary(
                kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
            var fields = new Dictionary<string, string>(ticket.CustomFields);
            foreach (var property in vendorFields.EnumerateObject())
            {
                if (canonicalByVendor.TryGetValue(property.Name, out var canonical))
                    fields[canonical] = property.Value.GetString() ?? "";
            }
            ticket = ticket with { CustomFields = fields };
        }

        _tickets[id] = ticket;
        return Json(HttpStatusCode.OK, Render(ticket));
    }

    private string Render(HelpdeskTicket ticket)
    {
        var customFields = ticket.CustomFields.ToDictionary(
            kv => fieldMap[kv.Key], kv => (object)kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = long.Parse(ticket.Id, CultureInfo.InvariantCulture),
            ["subject"] = ticket.Subject,
            ["description"] = ticket.BodyHtml,
            ["status"] = ticket.Status switch
            {
                TicketStatus.Open => 2,
                TicketStatus.Pending => 3,
                TicketStatus.Resolved => 4,
                TicketStatus.Closed => 5,
                _ => 3,
            },
            ["priority"] = (int)ticket.Priority + 1,
            ["created_at"] = ticket.CreatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["tags"] = ticket.Tags,
            ["custom_fields"] = customFields,
            ["requester"] = new Dictionary<string, string> { ["email"] = ticket.Sender },
        });
    }

    private static TicketStatus VendorStatus(int value) => value switch
    {
        2 => TicketStatus.Open,
        3 => TicketStatus.Pending,
        4 => TicketStatus.Resolved,
        5 => TicketStatus.Closed,
        _ => TicketStatus.Pending,
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [GeneratedRegex(@"/tickets/(\d+)$")]
    private static partial Regex TicketPath();

    [GeneratedRegex(@"/tickets/(\d+)/notes$")]
    private static partial Regex NotePath();
}
