using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Data.Configuration;

/// <summary>One column layout for SyncState everywhere it appears.</summary>
internal static class SyncStateMapping
{
    public static void Configure(ComplexPropertyBuilder<SyncState> sync)
    {
        sync.Property(s => s.Status).HasColumnName("sync_status");
        sync.Property(s => s.Error).HasColumnName("sync_error").HasMaxLength(2000);
        sync.Property(s => s.SyncedAt).HasColumnName("synced_at");
    }
}
