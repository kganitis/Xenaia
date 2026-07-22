using Microsoft.EntityFrameworkCore;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Stores;

namespace Xenaia.Data;

/// <summary>EF-backed repository for the Product/ParticipantType catalog.</summary>
public sealed class EfCatalogStore(XenaiaDbContext context) : ICatalogStore
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct)
        => await context.Products.Include(p => p.Options).ToListAsync(ct);

    public Task AddAsync(Product product, CancellationToken ct)
    {
        context.Products.Add(product);
        return Task.CompletedTask;
    }

    public Task AddParticipantTypeAsync(ParticipantType participantType, CancellationToken ct)
    {
        context.ParticipantTypes.Add(participantType);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ParticipantType>> GetParticipantTypesAsync(
        int productExternalId, int optionExternalId, CancellationToken ct)
    {
        // ParticipantType is keyed by the option's INTERNAL id, so resolve the
        // (product external id, option external id) pair to that internal id
        // first, then fetch the participant types that point at it.
        var optionId = await context.Products
            .Where(p => p.ExternalId == productExternalId)
            .SelectMany(p => p.Options)
            .Where(o => o.ExternalId == optionExternalId)
            .Select(o => o.Id)
            .SingleOrDefaultAsync(ct);
        if (optionId == 0)
            return [];

        return await context.ParticipantTypes
            .Where(pt => pt.ProductOptionId == optionId)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
