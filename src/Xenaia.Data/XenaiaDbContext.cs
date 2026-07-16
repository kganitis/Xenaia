using Microsoft.EntityFrameworkCore;
using Xenaia.Core.Outbox;

namespace Xenaia.Data;

/// <summary>
/// The single relational context. Grows one DbSet and one configuration
/// class per entity as later modules land; OnModelCreating stays a
/// one-liner (configurations are discovered from this assembly).
/// </summary>
public sealed class XenaiaDbContext(DbContextOptions<XenaiaDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(XenaiaDbContext).Assembly);
}
