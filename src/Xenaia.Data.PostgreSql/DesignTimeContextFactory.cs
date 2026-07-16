using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Xenaia.Data.PostgreSql;

/// <summary>
/// Design-time only: dotnet-ef needs a constructible context to generate
/// migrations. The connection string is a placeholder; codegen never
/// connects to a database.
/// </summary>
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<XenaiaDbContext>
{
    public XenaiaDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<XenaiaDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=design-time-placeholder",
                o => o.MigrationsAssembly("Xenaia.Data.PostgreSql"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
