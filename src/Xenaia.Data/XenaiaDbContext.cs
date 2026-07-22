using Microsoft.EntityFrameworkCore;
using Xenaia.Core.Outbox;
using Xenaia.Domain.Bookings.Availabilities;
using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Channels;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data;

/// <summary>
/// The single relational context. Grows one DbSet and one configuration
/// class per entity as later modules land; OnModelCreating stays a
/// one-liner (configurations are discovered from this assembly).
/// </summary>
public sealed class XenaiaDbContext(DbContextOptions<XenaiaDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Availability> Availabilities => Set<Availability>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Extra> Extras => Set<Extra>();

    public DbSet<ParticipantType> ParticipantTypes => Set<ParticipantType>();

    public DbSet<PaymentType> PaymentTypes => Set<PaymentType>();

    public DbSet<OutboundBookingRequest> OutboundBookingRequests => Set<OutboundBookingRequest>();

    public DbSet<SyncCheckpoint> SyncCheckpoints => Set<SyncCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(XenaiaDbContext).Assembly);
}
