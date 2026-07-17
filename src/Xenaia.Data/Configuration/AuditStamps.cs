using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Xenaia.Data.Configuration;

/// <summary>
/// Shadow audit stamps set by persistence (the AuditStampInterceptor),
/// never by domain code.
/// </summary>
internal static class AuditStamps
{
    public static void Configure<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<DateTimeOffset>("CreatedAt");
        builder.Property<DateTimeOffset>("UpdatedAt");
    }
}
