using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Xenaia.Data.Configuration;

/// <summary>
/// PostgreSQL xmin as the optimistic concurrency token, so competing sync
/// workers cannot both claim the same row. Recorded pre-v1 pragmatism:
/// this mapping is PostgreSQL-specific but lives here because only the
/// PostgreSQL provider exists today; it moves behind a provider seam when
/// a second provider arrives (spec 2026-07-17, section 5.3).
/// </summary>
internal static class ConcurrencyMapping
{
    public static void Configure<T>(EntityTypeBuilder<T> builder) where T : class
        => builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
}
