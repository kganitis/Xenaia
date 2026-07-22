using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Xenaia.Adapters.BrightTide;
using Xenaia.Adapters.BrightTide.Dtos;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Adapters.BrightTide.Tests;

/// <summary>
/// A stateful in-process fake of the BrightTide REST endpoints the adapter
/// uses, backed by plain dictionaries. State is held as port-shaped snapshots;
/// JSON is rendered vendor-shaped (snake_case DTOs) on the way out and parsed
/// vendor-shaped on the way in. It doubles as executable documentation of the
/// wire shape and lets the port contract run over real HTTP-shaped round-trips.
/// </summary>
public sealed partial class BrightTideFakeVendorHandler : HttpMessageHandler
{
    private readonly Dictionary<string, BookingSnapshot> _bookings = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ProductSnapshot> _products = [];
    private readonly Dictionary<int, List<ProductOptionSnapshot>> _options = [];
    private readonly Dictionary<(int Product, int Option), List<AvailabilityTimeslot>> _availability = [];
    private readonly Dictionary<string, PendingBooking> _pending = new(StringComparer.Ordinal);
    private int _nextCode = 1000;

    private sealed class PendingBooking
    {
        public required BrightTideStartRequestDto Start { get; init; }
        public List<BrightTideDraftItemDto> Items { get; set; } = [];
    }

    public void SeedProduct(ProductSnapshot product, params ProductOptionSnapshot[] options)
    {
        _products[product.ExternalId] = product;
        _options[product.ExternalId] = [.. options];
    }

    public void SeedBooking(BookingSnapshot booking) => _bookings[booking.Code] = booking;

    public void SeedAvailability(
        int productExternalId, int optionExternalId, params AvailabilityTimeslot[] slots) =>
        _availability[(productExternalId, optionExternalId)] = [.. slots];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

        if (method == HttpMethod.Post && path.EndsWith("/bookings/start", StringComparison.Ordinal))
            return StartBooking(body);
        if (method == HttpMethod.Post && path.EndsWith("/bookings/items", StringComparison.Ordinal))
            return AddItems(request, body);
        if (method == HttpMethod.Post && path.EndsWith("/bookings/complete", StringComparison.Ordinal))
            return CompleteBooking(body);

        var cancelMatch = CancelPath().Match(path);
        if (method == HttpMethod.Post && cancelMatch.Success)
            return CancelBooking(Uri.UnescapeDataString(cancelMatch.Groups[1].Value));

        var availabilityMatch = AvailabilityPath().Match(path);
        if (method == HttpMethod.Get && availabilityMatch.Success)
            return GetAvailability(
                int.Parse(availabilityMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(availabilityMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                request.RequestUri.Query);

        if (method == HttpMethod.Patch && path.EndsWith("/products/availability", StringComparison.Ordinal))
            return PatchAvailability(body);

        var optionsMatch = OptionsPath().Match(path);
        if (method == HttpMethod.Get && optionsMatch.Success)
            return GetOptions(int.Parse(optionsMatch.Groups[1].Value, CultureInfo.InvariantCulture));

        if (method == HttpMethod.Get && path.EndsWith("/products", StringComparison.Ordinal))
            return GetProducts();

        var codeMatch = BookingCodePath().Match(path);
        if (method == HttpMethod.Get && codeMatch.Success)
            return GetBookingByCode(Uri.UnescapeDataString(codeMatch.Groups[1].Value));

        if (method == HttpMethod.Get && path.EndsWith("/bookings", StringComparison.Ordinal))
            return ListBookings(request.RequestUri.Query);

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private HttpResponseMessage StartBooking(string body)
    {
        var start = JsonSerializer.Deserialize<BrightTideStartRequestDto>(body, BrightTideClient.Json)!;
        var code = $"MT-{_nextCode++}";
        _pending[code] = new PendingBooking { Start = start };
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
            new BrightTideStartResponseDto { Code = code, SecretCode = $"SEC-{code}" }, BrightTideClient.Json));
    }

    private HttpResponseMessage AddItems(HttpRequestMessage request, string body)
    {
        var items = JsonSerializer.Deserialize<BrightTideItemsRequestDto>(body, BrightTideClient.Json)!;
        if (!_pending.TryGetValue(items.Code, out var pending))
            return Json(HttpStatusCode.NotFound, "{}");
        if (!request.Headers.TryGetValues("Secret-Code", out var secret)
            || secret.SingleOrDefault() != $"SEC-{items.Code}")
            return Json(HttpStatusCode.Unauthorized, "{}");
        pending.Items = items.Items;
        return Json(HttpStatusCode.OK, "{}");
    }

    private HttpResponseMessage CompleteBooking(string body)
    {
        var complete = JsonSerializer.Deserialize<BrightTideCompleteRequestDto>(body, BrightTideClient.Json)!;
        if (!_pending.TryGetValue(complete.Code, out var pending))
            return Json(HttpStatusCode.NotFound, "{}");

        var now = DateTimeOffset.UtcNow;
        var snapshot = new BookingSnapshot
        {
            Code = complete.Code,
            SecretCode = $"SEC-{complete.Code}",
            Type = BrightTideMapping.MapType(pending.Start.BookingType),
            Status = BookingStatus.Pending,
            FinalPrice = pending.Items.Sum(i => i.FinalPrice),
            Referrer = pending.Start.Referrer,
            LeadContactName = pending.Start.LeadContactName,
            Email = pending.Start.Email,
            Phone = pending.Start.Phone,
            ActivityLanguage = pending.Start.ActivityLanguage,
            CreatedAtExternal = now,
            UpdatedAtExternal = now,
            Items = pending.Items.Select((i, index) => new BookingItemSnapshot(
                index + 1, i.ProductId, i.ProductOptionId, i.ParticipantTypeAlias,
                BrightTideMapping.ParseDate(i.ActivityDateTime) ?? default, i.FinalPrice)).ToList(),
        };
        _bookings[complete.Code] = snapshot;
        _pending.Remove(complete.Code);
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(ToDto(snapshot), BrightTideClient.Json));
    }

    private HttpResponseMessage CancelBooking(string code)
    {
        if (!_bookings.TryGetValue(code, out var booking))
            return Json(HttpStatusCode.NotFound, "{}");
        _bookings[code] = booking with
        {
            Status = BookingStatus.Cancelled,
            CancelledAt = DateTimeOffset.UtcNow,
        };
        return Json(HttpStatusCode.OK, "{}");
    }

    private HttpResponseMessage GetBookingByCode(string code) =>
        _bookings.TryGetValue(code, out var booking)
            ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(ToDto(booking), BrightTideClient.Json))
            : Json(HttpStatusCode.NotFound, "{}");

    private HttpResponseMessage ListBookings(string query)
    {
        var q = HttpUtility.ParseQueryString(query);
        IEnumerable<BookingSnapshot> result = _bookings.Values;

        if (q["booking_status"] is { } status)
            result = result.Where(b => b.Status.ToString().ToLowerInvariant() == status);
        if (q["booking_type"] is { } type)
            result = result.Where(b => b.Type.ToString().ToLowerInvariant() == type);
        if (q["referrer"] is { } referrer)
            result = result.Where(b => string.Equals(b.Referrer, referrer, StringComparison.Ordinal));
        if (ParseDay(q["update_date_time_from"]) is { } uf)
            result = result.Where(b => b.UpdatedAtExternal is { } u && u >= uf);
        if (ParseDay(q["update_date_time_to"]) is { } ut)
            result = result.Where(b => b.UpdatedAtExternal is { } u && u <= ut);
        if (ParseDay(q["booking_date_time_from"]) is { } bf)
            result = result.Where(b => b.CreatedAtExternal is { } c && c >= bf);
        if (ParseDay(q["booking_date_time_to"]) is { } bt)
            result = result.Where(b => b.CreatedAtExternal is { } c && c <= bt);

        var dtos = result.Select(ToDto).ToList();
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(dtos, BrightTideClient.Json));
    }

    private HttpResponseMessage GetAvailability(int productId, int optionId, string query)
    {
        if (!_availability.TryGetValue((productId, optionId), out var slots))
            return Json(HttpStatusCode.NotFound, "{}");

        var q = HttpUtility.ParseQueryString(query);
        var from = ParseInstant(q["from"]);
        var to = ParseInstant(q["to"]);
        var dtos = slots
            .Where(s => (from is null || s.At >= from) && (to is null || s.At <= to))
            .Select(s => new BrightTideAvailabilityDto
            {
                DateTime = s.At.ToString("O", CultureInfo.InvariantCulture),
                Vacancies = s.Vacancies,
            })
            .ToList();
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(dtos, BrightTideClient.Json));
    }

    private HttpResponseMessage PatchAvailability(string body)
    {
        var patch = JsonSerializer.Deserialize<BrightTideAvailabilityPatchDto>(body, BrightTideClient.Json)!;
        foreach (var update in patch.Updates)
        {
            // Null vacancies is a signal-only update: leave the combination as
            // it was, and never register a previously unknown product/option.
            if (update.Vacancies is not { } vacancies)
                continue;

            var key = (update.ProductId, update.ProductOptionId);
            if (!_availability.TryGetValue(key, out var slots))
            {
                slots = [];
                _availability[key] = slots;
            }

            foreach (var at in TargetInstants(update))
            {
                var index = slots.FindIndex(s => s.At == at);
                var slot = new AvailabilityTimeslot(at, vacancies);
                if (index >= 0)
                    slots[index] = slot;
                else
                    slots.Add(slot);
            }
        }
        return Json(HttpStatusCode.OK, "{}");
    }

    private HttpResponseMessage GetProducts()
    {
        var dtos = _products.Values
            .Select(p => new BrightTideProductDto { Id = p.ExternalId, Title = p.Title, CategoryId = p.CategoryExternalId })
            .ToList();
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(dtos, BrightTideClient.Json));
    }

    private HttpResponseMessage GetOptions(int productId)
    {
        var dtos = (_options.TryGetValue(productId, out var found) ? found : [])
            .Select(o => new BrightTideProductOptionDto
            {
                Id = o.ExternalId,
                Title = o.Title,
                ParticipantTypes = o.ParticipantTypes
                    .Select(pt => new BrightTideParticipantTypeDto { Alias = pt.Alias, Title = pt.Title })
                    .ToList(),
            })
            .ToList();
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(dtos, BrightTideClient.Json));
    }

    private static IEnumerable<DateTimeOffset> TargetInstants(BrightTideAvailabilityUpdateDto update)
    {
        var from = ParseInstant(update.From)!.Value;
        var to = ParseInstant(update.To)!.Value;
        if (update.Times is not { Count: > 0 } times)
        {
            yield return from;
            yield break;
        }

        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var parsed = TimeOnly.Parse(time, CultureInfo.InvariantCulture);
                var at = new DateTimeOffset(day.Add(parsed.ToTimeSpan()), from.Offset);
                if (at >= from && at <= to)
                    yield return at;
            }
        }
    }

    private static BrightTideBookingDto ToDto(BookingSnapshot b) => new()
    {
        Code = b.Code,
        SecretCode = b.SecretCode,
        BookingType = b.Type.ToString().ToLowerInvariant(),
        BookingStatus = b.Status.ToString().ToLowerInvariant(),
        FinalPrice = b.FinalPrice,
        Referrer = b.Referrer,
        ChannelBookingCode = b.ChannelBookingCode,
        LeadContactName = b.LeadContactName,
        Email = b.Email,
        Phone = b.Phone,
        ActivityLanguage = b.ActivityLanguage,
        CreatedDateTime = Iso(b.CreatedAtExternal),
        UpdateDateTime = Iso(b.UpdatedAtExternal),
        CancelledDateTime = Iso(b.CancelledAt),
        Items = b.Items.Select(i => new BrightTideBookingItemDto
        {
            Id = i.ExternalId,
            ProductId = i.ProductExternalId,
            ProductOptionId = i.OptionExternalId,
            ParticipantTypeAlias = i.ParticipantTypeAlias,
            ActivityDateTime = Iso(i.ActivityAt),
            FinalPrice = i.FinalPrice,
        }).ToList(),
    };

    private static string? Iso(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDay(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [GeneratedRegex(@"/bookings/(.+)/cancel$")]
    private static partial Regex CancelPath();

    [GeneratedRegex(@"/bookings/([^/]+)$")]
    private static partial Regex BookingCodePath();

    [GeneratedRegex(@"/products/(\d+)/options/(\d+)/availability$")]
    private static partial Regex AvailabilityPath();

    [GeneratedRegex(@"/products/(\d+)/options$")]
    private static partial Regex OptionsPath();
}
