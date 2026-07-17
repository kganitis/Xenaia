using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Channels;

namespace Xenaia.Data.Configuration;

public sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(128);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Title).HasMaxLength(256);

        builder.ComplexProperty(c => c.Sync, SyncStateMapping.Configure);
        AuditStamps.Configure(builder);
        ConcurrencyMapping.Configure(builder);
    }
}
