using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Xenaia.Data.Configuration;

public sealed class SyncCheckpointConfiguration : IEntityTypeConfiguration<SyncCheckpoint>
{
    public void Configure(EntityTypeBuilder<SyncCheckpoint> builder)
    {
        builder.HasKey(c => c.Name);
        builder.Property(c => c.Name).HasMaxLength(128);
    }
}
