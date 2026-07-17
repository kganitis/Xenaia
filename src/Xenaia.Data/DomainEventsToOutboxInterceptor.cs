using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xenaia.Core.Domain;
using Xenaia.Core.Outbox;

namespace Xenaia.Data;

/// <summary>
/// Drains raised domain events from tracked aggregates into outbox rows
/// inside the same SaveChanges, completing the transactional outbox that
/// the Core drainer services.
/// </summary>
public sealed class DomainEventsToOutboxInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Drain(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Drain(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Drain(DbContext? context)
    {
        if (context is null) return;

        var messages = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IHasDomainEvents)
            .SelectMany(entry => ((IHasDomainEvents)entry.Entity).DequeueDomainEvents())
            .Select(OutboxMessage.From)
            .ToList();

        if (messages.Count > 0)
            context.Set<OutboxMessage>().AddRange(messages);
    }
}
