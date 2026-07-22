using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xenaia.Adapters.BrightTide.Dtos;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Adapters.BrightTide;

/// <summary>
/// IBookingSystemProvider over the BrightTide REST API. Vendor DTOs, snake_case
/// wire shapes, the three-step create dance, and the vendor's date formats never
/// leak past this class. The static API-Key header, base address, timeout, and
/// resilience handler are configured on the injected typed HttpClient by
/// <see cref="BrightTideServiceCollectionExtensions"/>.
/// </summary>
public sealed class BrightTideClient(HttpClient http) : IBookingSystemProvider
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<BookingSnapshot>> GetBookingsAsync(
        BookingQuery query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "bookings" + BuildQuery(query));
        using var response = await SendAsync(request, ct);
        await ThrowIfUnhandled(response, ct);
        var dtos = await response.Content.ReadFromJsonAsync<List<BrightTideBookingDto>>(Json, ct) ?? [];
        return dtos.Select(BrightTideMapping.ToSnapshot).ToList();
    }

    public async Task<BookingSnapshot?> GetBookingByCodeAsync(string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"bookings/{Uri.EscapeDataString(code)}");
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await ThrowIfUnhandled(response, ct);
        var dto = await response.Content.ReadFromJsonAsync<BrightTideBookingDto>(Json, ct);
        return dto is null ? null : BrightTideMapping.ToSnapshot(dto);
    }

    public async Task<BookingSnapshot> CreateBookingAsync(BookingDraft draft, CancellationToken ct)
    {
        draft.EnsureValid();

        // Step 1: start the booking; BrightTide assigns the code and secret.
        var start = await PostForAsync<BrightTideStartResponseDto>(
            "bookings/start",
            new BrightTideStartRequestDto
            {
                BookingType = BrightTideMapping.ToVendorToken(draft.Type),
                LeadContactName = draft.LeadContactName,
                Email = draft.Email,
                Phone = draft.Phone,
                Referrer = draft.Referrer,
                ActivityLanguage = draft.ActivityLanguage,
            },
            ct);

        if (start?.Code is not { Length: > 0 } code)
            throw new BookingSystemException("BrightTide bookings/start returned no booking code.");

        // Step 2: attach the items, authenticated by the Secret-Code header.
        using var itemsRequest = new HttpRequestMessage(HttpMethod.Post, "bookings/items")
        {
            Content = JsonContent.Create(
                new BrightTideItemsRequestDto
                {
                    Code = code,
                    Items = draft.Items.Select(i => new BrightTideDraftItemDto
                    {
                        ProductId = i.ProductExternalId,
                        ProductOptionId = i.OptionExternalId,
                        ParticipantTypeAlias = i.ParticipantTypeAlias,
                        ActivityDateTime = Instant(i.ActivityAt),
                        FinalPrice = i.FinalPrice,
                    }).ToList(),
                },
                options: Json),
        };
        itemsRequest.Headers.Add("Secret-Code", start.SecretCode ?? "");
        using var itemsResponse = await SendAsync(itemsRequest, ct);
        await ThrowIfUnhandled(itemsResponse, ct);

        // Step 3: complete the booking; the vendor returns the full payload.
        var completed = await PostForAsync<BrightTideBookingDto>(
            "bookings/complete", new BrightTideCompleteRequestDto { Code = code }, ct);
        return completed is null
            ? throw new BookingSystemException("BrightTide bookings/complete returned an empty payload.")
            : BrightTideMapping.ToSnapshot(completed);
    }

    public async Task CancelBookingAsync(string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"bookings/{Uri.EscapeDataString(code)}/cancel");
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new BookingSystemEntityNotFoundException($"BrightTide does not know booking code '{code}'.");
        await ThrowIfUnhandled(response, ct);
    }

    public async Task<IReadOnlyList<AvailabilityTimeslot>?> GetAvailabilityAsync(
        int productExternalId, int optionExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var url = $"products/{productExternalId}/options/{optionExternalId}/availability"
            + $"?from={Instant(from)}&to={Instant(to)}&pricing=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode == HttpStatusCode.BadRequest)
            return [];
        await ThrowIfUnhandled(response, ct);
        var dtos = await response.Content.ReadFromJsonAsync<List<BrightTideAvailabilityDto>>(Json, ct) ?? [];
        return dtos
            .Select(BrightTideMapping.ToTimeslot)
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .ToList();
    }

    public async Task UpdateAvailabilityAsync(AvailabilityUpdate update, CancellationToken ct)
    {
        var patch = new BrightTideAvailabilityPatchDto
        {
            Updates =
            [
                new BrightTideAvailabilityUpdateDto
                {
                    ProductId = update.ProductExternalId,
                    ProductOptionId = update.OptionExternalId,
                    From = Instant(update.From),
                    To = Instant(update.To),
                    Times = update.Times?
                        .Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))
                        .ToList(),
                    Vacancies = update.Vacancies,
                    StopSales = update.StopSales,
                    ParticipantTypes = [.. update.ParticipantTypeAliases],
                },
            ],
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, "products/availability")
        {
            Content = JsonContent.Create(patch, options: Json),
        };
        using var response = await SendAsync(request, ct);
        await ThrowIfUnhandled(response, ct);
    }

    public async Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "products?content=false");
        using var response = await SendAsync(request, ct);
        await ThrowIfUnhandled(response, ct);
        var dtos = await response.Content.ReadFromJsonAsync<List<BrightTideProductDto>>(Json, ct) ?? [];
        return dtos.Select(BrightTideMapping.ToSnapshot).ToList();
    }

    public async Task<IReadOnlyList<ProductOptionSnapshot>> GetProductOptionsAsync(
        int productExternalId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"products/{productExternalId}/options?content=false");
        using var response = await SendAsync(request, ct);
        await ThrowIfUnhandled(response, ct);
        var dtos = await response.Content.ReadFromJsonAsync<List<BrightTideProductOptionDto>>(Json, ct) ?? [];
        return dtos.Select(BrightTideMapping.ToSnapshot).ToList();
    }

    private async Task<T?> PostForAsync<T>(string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        };
        using var response = await SendAsync(request, ct);
        await ThrowIfUnhandled(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BookingSystemException("BrightTide request failed.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BookingSystemException("BrightTide request timed out.", ex);
        }
        catch (Polly.ExecutionRejectedException ex)
        {
            // The standard resilience handler surfaces an open circuit
            // (BrokenCircuitException) or an attempt/total timeout
            // (TimeoutRejectedException) as raw Polly types; the port contract
            // requires transport failures to arrive as BookingSystemException.
            throw new BookingSystemException("BrightTide resilience pipeline rejected the request.", ex);
        }
    }

    private static async Task ThrowIfUnhandled(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new BrightTideApiException((int)response.StatusCode, body);
    }

    private static string BuildQuery(BookingQuery query)
    {
        var parts = new List<string>();
        if (query.Type is { } type)
            parts.Add($"booking_type={BrightTideMapping.ToVendorToken(type)}");
        if (query.Status is { } status)
            parts.Add($"booking_status={BrightTideMapping.ToVendorToken(status)}");
        if (query.Referrer is { } referrer)
            parts.Add($"referrer={Uri.EscapeDataString(referrer)}");
        if (query.BookedFrom is { } bookedFrom)
            parts.Add($"booking_date_time_from={Day(bookedFrom)}");
        if (query.BookedTo is { } bookedTo)
            parts.Add($"booking_date_time_to={Day(bookedTo)}");
        if (query.ActivityFrom is { } activityFrom)
            parts.Add($"activity_date_time_from={Day(activityFrom)}");
        if (query.ActivityTo is { } activityTo)
            parts.Add($"activity_date_time_to={Day(activityTo)}");
        if (query.UpdatedFrom is { } updatedFrom)
            parts.Add($"update_date_time_from={Day(updatedFrom)}");
        if (query.UpdatedTo is { } updatedTo)
            parts.Add($"update_date_time_to={Day(updatedTo)}");
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static string Day(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Instant(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
