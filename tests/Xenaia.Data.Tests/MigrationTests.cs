using Microsoft.EntityFrameworkCore;

namespace Xenaia.Data.Tests;

[Collection("postgres")]
public class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Initial_create_is_the_single_applied_migration()
    {
        await using var context = fixture.CreateContext();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();

        Assert.Single(applied);
        Assert.EndsWith("InitialCreate", applied[0]);
    }

    [Fact]
    public async Task Model_has_no_pending_changes_against_the_snapshot()
    {
        await using var context = fixture.CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
