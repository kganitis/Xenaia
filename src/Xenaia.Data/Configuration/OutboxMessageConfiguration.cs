using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Core.Outbox;

namespace Xenaia.Data.Configuration;

/// <summary>
/// Table and column names come from the provider-level snake_case naming
/// convention; configurations never hard-code names. The filtered index
/// matches EfOutboxStore's unprocessed predicate so drainer scans stay
/// cheap as the table grows. The filter SQL references the snake_case
/// column names the convention produces.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).IsRequired().HasMaxLength(512);
        builder.Property(m => m.Payload).IsRequired();
        builder.HasIndex(m => m.OccurredAt)
            .HasDatabaseName("ix_outbox_unprocessed")
            .HasFilter("processed_at IS NULL AND error IS NULL");
    }
}
