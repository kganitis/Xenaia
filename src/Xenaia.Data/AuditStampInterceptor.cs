using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Xenaia.Data;

/// <summary>
/// Sets the shadow CreatedAt/UpdatedAt stamps declared by AuditStamps.
/// Persistence owns wall-clock time; domain code never touches it.
/// </summary>
public sealed class AuditStampInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty("CreatedAt") is null) continue;

            if (entry.State == EntityState.Added)
                entry.Property("CreatedAt").CurrentValue = now;
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property("UpdatedAt").CurrentValue = now;
        }
    }
}
