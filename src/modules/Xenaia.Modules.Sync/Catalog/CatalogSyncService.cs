using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Modules.Sync.Catalog;

/// <summary>The outcome of one CatalogSyncService.RefreshAsync call.</summary>
public sealed record CatalogSyncSummary(
    int ProductsSeen, int ProductsAdded, int OptionsAdded, int ParticipantTypesAdded);

/// <summary>
/// Catalog sync (spec 6.5): pulls every product from the booking system,
/// self-throttled between products by <c>Sync:Catalog:FetchDelayMs</c>, and
/// upserts into the local catalog aggregates through <see cref="ICatalogStore"/>.
/// New products are born via <see cref="Product.Define"/>; existing ones are
/// retitled/recategorized and gain any new options (existing options are
/// never mutated: <see cref="ProductOption"/> exposes no such operation).
/// Participant types are upserted per option, matched by alias so a type the
/// vendor still reports is never duplicated. Every touched row (and its new
/// participant types) finishes <c>Synced</c>. Scoped: a fresh instance per
/// <see cref="CatalogRefreshService"/> tick or refresh endpoint call.
/// </summary>
public sealed class CatalogSyncService(
    IBookingSystemProvider provider,
    ICatalogStore store,
    ParticipantTypeCache participantTypeCache,
    IOptions<SyncOptions> options,
    TimeProvider clock,
    ILogger<CatalogSyncService> logger,
    Func<TimeSpan, CancellationToken, Task>? delayer = null)
{
    private readonly SyncOptions _options = options.Value;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer =
        delayer ?? ((delay, token) => Task.Delay(delay, clock, token));

    public async Task<CatalogSyncSummary> RefreshAsync(CancellationToken ct)
    {
        var vendorProducts = await provider.GetProductsAsync(ct);
        var existingByExternalId = (await store.GetProductsAsync(ct))
            .ToDictionary(p => p.ExternalId);

        var productsSeen = 0;
        var productsAdded = 0;
        var optionsAdded = 0;
        var participantTypesAdded = 0;

        for (var i = 0; i < vendorProducts.Count; i++)
        {
            if (i > 0)
                await _delayer(TimeSpan.FromMilliseconds(_options.Catalog.FetchDelayMs), ct);

            var vendorProduct = vendorProducts[i];
            productsSeen++;

            var vendorOptions = await provider.GetProductOptionsAsync(vendorProduct.ExternalId, ct);

            Product product;
            bool isNew;
            if (existingByExternalId.TryGetValue(vendorProduct.ExternalId, out var existingProduct))
            {
                isNew = false;
                product = existingProduct;
                product.Retitle(vendorProduct.Title);
                product.Recategorize(vendorProduct.CategoryExternalId);
            }
            else
            {
                isNew = true;
                product = Product.Define(vendorProduct.ExternalId, vendorProduct.Title, vendorProduct.CategoryExternalId);
                productsAdded++;
            }

            if (product.Sync.Status != SyncStatus.Pending)
                product.RequeueSync();
            product.ClaimForSync();

            var existingOptionExternalIds = product.Options.Select(o => o.ExternalId).ToHashSet();
            foreach (var vendorOption in vendorOptions)
            {
                if (existingOptionExternalIds.Contains(vendorOption.ExternalId))
                    continue;
                product.AddOption(vendorOption.ExternalId, vendorOption.Title);
                optionsAdded++;
            }

            if (isNew)
                await store.AddAsync(product, ct);

            var now = clock.GetUtcNow();
            product.MarkSynced(now);
            await store.SaveChangesAsync(ct);

            foreach (var vendorOption in vendorOptions)
            {
                var productOption = product.Options.First(o => o.ExternalId == vendorOption.ExternalId);
                var existingTypes = await store.GetParticipantTypesAsync(
                    vendorProduct.ExternalId, vendorOption.ExternalId, ct);
                var existingAliases = existingTypes.Select(t => t.Alias).ToHashSet(StringComparer.Ordinal);

                foreach (var vendorType in vendorOption.ParticipantTypes)
                {
                    if (existingAliases.Contains(vendorType.Alias))
                        continue;

                    var participantType = ParticipantType.Define(productOption.Id, vendorType.Alias, vendorType.Title);
                    participantType.ClaimForSync();
                    participantType.MarkSynced(now);
                    await store.AddParticipantTypeAsync(participantType, ct);
                    participantTypesAdded++;
                }
            }

            await store.SaveChangesAsync(ct);
        }

        participantTypeCache.Invalidate();

        logger.LogInformation(
            "Catalog refresh: {ProductsSeen} products seen, {ProductsAdded} added, " +
            "{OptionsAdded} options added, {ParticipantTypesAdded} participant types added",
            productsSeen, productsAdded, optionsAdded, participantTypesAdded);

        return new CatalogSyncSummary(productsSeen, productsAdded, optionsAdded, participantTypesAdded);
    }
}
