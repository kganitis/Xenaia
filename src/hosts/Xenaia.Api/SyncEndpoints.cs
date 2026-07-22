using Xenaia.Domain.Bookings.Providers;
using Xenaia.Modules.Sync.Availability;
using Xenaia.Modules.Sync.Bookings;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Api;

/// <summary>
/// The Sync module's REST ingestion surface (spec section 10). Mapped only when
/// the Sync module is composed (a booking system is configured); the API-key
/// gate protects every route. Handlers stay thin: they translate request bodies
/// into the module's service contracts and map the documented failures onto
/// status codes (400/404/409/503), leaving all logic in the module.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Availability outbound (spec 6.1): 400 on structural garbage or a batch
        // over Sync:Availability:MaxBatchSize; 200 with the patch result.
        api.MapPost("/availability/patch", async (
            AvailabilityPatchRequest request, AvailabilityPatchService service, CancellationToken ct) =>
        {
            if (request.Items is null)
                return Results.BadRequest("items is required.");

            var items = request.Items
                .Select(i => new AvailabilityPatchItem(
                    i.ProductExternalId, i.OptionExternalId, i.From, i.To,
                    i.Times ?? [], i.Vacancies, i.StopSales, i.PatchStatusRange))
                .ToList();

            try
            {
                var result = await service.EnqueueAsync(items, request.SpreadsheetId, request.Force, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Availability inbound (spec 6.2): 503 when no spreadsheet provider is
        // registered (the fetch service requires one); otherwise 200 with the
        // sheet-sync summary. The gateway is checked before the service is
        // resolved, because resolving it without a gateway would throw.
        api.MapPost("/availability/sync", async (
            AvailabilitySyncRequest request, HttpContext http, CancellationToken ct) =>
        {
            if (http.RequestServices.GetService<ISpreadsheetGateway>() is null)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var service = http.RequestServices.GetRequiredService<AvailabilityFetchService>();
            var summary = await service.SyncFromSheetAsync(request.SpreadsheetId, ct);
            return Results.Ok(summary);
        });

        // Bookings outbound create (spec 6.4): 202 with the request id; a
        // structurally invalid draft or an unknown product/option -> 400.
        api.MapPost("/bookings", async (
            BookingDraft draft, OutboundBookingEnqueuer enqueuer, CancellationToken ct) =>
        {
            try
            {
                var requestId = await enqueuer.EnqueueCreateAsync(draft, ct);
                return Results.Json(new { requestId }, statusCode: StatusCodes.Status202Accepted);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Bookings outbound cancel (spec 6.4): 202 with the request id; unknown
        // code -> 404, already cancelled -> 409.
        api.MapPost("/bookings/{code}/cancel", async (
            string code, OutboundBookingEnqueuer enqueuer, CancellationToken ct) =>
        {
            try
            {
                var requestId = await enqueuer.EnqueueCancelAsync(code, ct);
                return Results.Json(new { requestId }, statusCode: StatusCodes.Status202Accepted);
            }
            catch (UnknownBookingException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (BookingAlreadyCancelledException ex)
            {
                return Results.Conflict(ex.Message);
            }
        });

        // Catalog sync (spec 6.5): on-demand refresh, 200 with the summary.
        api.MapPost("/catalog/refresh", async (CatalogSyncService service, CancellationToken ct) =>
            Results.Ok(await service.RefreshAsync(ct)));
    }
}

/// <summary>Body of <c>POST /api/availability/patch</c>. Items map one-to-one
/// onto <see cref="AvailabilityPatchItem"/>; SpreadsheetId and per-item
/// PatchStatusRange are the optional sheet write-back context.</summary>
public sealed record AvailabilityPatchRequest(
    string? SpreadsheetId, bool Force, IReadOnlyList<AvailabilityPatchItemRequest>? Items);

public sealed record AvailabilityPatchItemRequest(
    int ProductExternalId, int OptionExternalId,
    DateOnly From, DateOnly To, IReadOnlyList<TimeOnly>? Times,
    int? Vacancies, bool? StopSales, string? PatchStatusRange);

/// <summary>Body of <c>POST /api/availability/sync</c>.</summary>
public sealed record AvailabilitySyncRequest(string SpreadsheetId);
