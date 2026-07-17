using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Adapters.Freshdesk;

/// <summary>
/// IHelpdeskProvider over the Freshdesk v2 REST API. Vendor DTOs, integer
/// status/priority values, cf_* field names, and pagination never leak past
/// this class. Uses the ticket list endpoint (not search): search cannot
/// include descriptions and carries tighter rate limits.
/// </summary>
public sealed class FreshdeskHelpdeskProvider(
    HttpClient http,
    IOptions<FreshdeskOptions> options,
    ILogger<FreshdeskHelpdeskProvider> logger) : IHelpdeskProvider
{
    private const int MaxPages = 300;

    private readonly Dictionary<string, string> _canonicalByVendor = options.Value.FieldMap
        .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<HelpdeskTicket>> GetOpenTicketsAsync(CancellationToken ct)
    {
        var pageSize = options.Value.PageSize;
        var tickets = new List<HelpdeskTicket>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var endpoint = "tickets?order_by=created_at&order_type=asc"
                + $"&include=description,requester&per_page={pageSize}&page={page}";
            var dtos = await http.GetFromJsonAsync<List<FreshdeskTicketDto>>(endpoint, ct) ?? [];
            tickets.AddRange(dtos.Where(d => d.Status == 2).Select(Map));
            if (dtos.Count < pageSize) break;
        }
        logger.LogInformation("Fetched {Count} open tickets from Freshdesk", tickets.Count);
        return tickets;
    }

    public async Task UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken ct)
    {
        List<string>? tags = null;
        if (update.AddTags.Count > 0)
        {
            var current = await GetTicketDtoAsync(ticketId, ct);
            tags = current.Tags ?? [];
            foreach (var tag in update.AddTags)
            {
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    tags.Add(tag);
            }
        }

        var body = new Dictionary<string, object?>();
        if (update.Status is { } status) body["status"] = StatusToVendor(status);
        if (update.Priority is { } priority) body["priority"] = PriorityToVendor(priority);
        if (tags is not null) body["tags"] = tags;
        if (update.SetCustomFields.Count > 0)
        {
            var vendorFields = new Dictionary<string, string>();
            foreach (var (canonical, value) in update.SetCustomFields)
            {
                if (!options.Value.FieldMap.TryGetValue(canonical, out var vendorName))
                {
                    throw new InvalidOperationException(
                        $"No Freshdesk mapping for canonical custom field '{canonical}'. "
                        + "Add it to Adapters:Freshdesk:FieldMap.");
                }
                vendorFields[vendorName] = value;
            }
            body["custom_fields"] = vendorFields;
        }
        if (body.Count == 0) return;

        using var response = await http.PutAsJsonAsync($"tickets/{ticketId}", body, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HelpdeskTicketNotFoundException(ticketId);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddPrivateNoteAsync(string ticketId, string htmlBody, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"tickets/{ticketId}/notes",
            new Dictionary<string, object?> { ["body"] = htmlBody, ["private"] = true },
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HelpdeskTicketNotFoundException(ticketId);
        response.EnsureSuccessStatusCode();
    }

    private async Task<FreshdeskTicketDto> GetTicketDtoAsync(string ticketId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"tickets/{ticketId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HelpdeskTicketNotFoundException(ticketId);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FreshdeskTicketDto>(ct)
            ?? throw new InvalidOperationException($"Empty Freshdesk response for ticket {ticketId}.");
    }

    private HelpdeskTicket Map(FreshdeskTicketDto dto)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (vendorName, value) in dto.CustomFields ?? [])
        {
            if (!_canonicalByVendor.TryGetValue(vendorName, out var canonical))
                continue;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
            if (!string.IsNullOrEmpty(text))
                fields[canonical] = text;
        }

        return new HelpdeskTicket
        {
            Id = dto.Id.ToString(CultureInfo.InvariantCulture),
            Subject = dto.Subject ?? "",
            BodyHtml = dto.Description ?? "",
            Sender = dto.Requester?.Email ?? "",
            Channel = fields.GetValueOrDefault("channel", ""),
            CreatedAt = dto.CreatedAt,
            Status = MapStatus(dto.Status),
            Priority = MapPriority(dto.Priority),
            Tags = dto.Tags ?? [],
            CustomFields = fields,
        };
    }

    private TicketStatus MapStatus(int vendor)
    {
        switch (vendor)
        {
            case 2: return TicketStatus.Open;
            case 3: return TicketStatus.Pending;
            case 4: return TicketStatus.Resolved;
            case 5: return TicketStatus.Closed;
            default:
                logger.LogWarning("Unknown Freshdesk status {Status}; treating as Pending", vendor);
                return TicketStatus.Pending;
        }
    }

    private static int StatusToVendor(TicketStatus status) => status switch
    {
        TicketStatus.Open => 2,
        TicketStatus.Pending => 3,
        TicketStatus.Resolved => 4,
        TicketStatus.Closed => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private TicketPriority MapPriority(int vendor)
    {
        switch (vendor)
        {
            case 1: return TicketPriority.Low;
            case 2: return TicketPriority.Medium;
            case 3: return TicketPriority.High;
            case 4: return TicketPriority.Urgent;
            default:
                logger.LogWarning("Unknown Freshdesk priority {Priority}; treating as Low", vendor);
                return TicketPriority.Low;
        }
    }

    private static int PriorityToVendor(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => 1,
        TicketPriority.Medium => 2,
        TicketPriority.High => 3,
        TicketPriority.Urgent => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };
}
