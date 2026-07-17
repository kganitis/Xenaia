using Xenaia.Modules.Triage.Helpdesk;
using Xunit;

namespace Xenaia.PortContracts.Helpdesk;

/// <summary>
/// Reusable behavioral contract for IHelpdeskProvider. Any adapter must pass
/// this suite; inherit it, implement the harness hooks, and the port's
/// semantics are asserted for free (design section 8, port contract tests).
/// </summary>
public abstract class HelpdeskProviderContract
{
    /// <summary>Creates a provider whose backing store contains exactly the
    /// seeded tickets.</summary>
    protected abstract Task<IHelpdeskProvider> CreateProviderAsync(
        IReadOnlyList<HelpdeskTicket> seed);

    /// <summary>Current state of a seeded ticket in the backing store.</summary>
    protected abstract Task<HelpdeskTicket> GetTicketSnapshotAsync(string id);

    /// <summary>Private notes created on a seeded ticket, oldest first.</summary>
    protected abstract Task<IReadOnlyList<string>> GetNotesAsync(string id);

    protected static HelpdeskTicket Ticket(
        string id,
        TicketStatus status = TicketStatus.Open,
        DateTimeOffset? createdAt = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, string>? customFields = null) => new()
    {
        Id = id,
        Subject = $"Ticket {id}",
        Status = status,
        Priority = TicketPriority.Low,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
        Tags = tags ?? [],
        CustomFields = customFields ?? new Dictionary<string, string>(),
    };

    [Fact]
    public async Task Open_tickets_come_back_open_only_and_oldest_first()
    {
        var provider = await CreateProviderAsync(
        [
            Ticket("2", createdAt: new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero)),
            Ticket("3", status: TicketStatus.Closed),
            Ticket("1", createdAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
        ]);

        var open = await provider.GetOpenTicketsAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], open.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Update_sets_status_and_priority()
    {
        var provider = await CreateProviderAsync([Ticket("1")]);

        await provider.UpdateTicketAsync("1", new TicketUpdate
        {
            Status = TicketStatus.Resolved,
            Priority = TicketPriority.Urgent,
        }, CancellationToken.None);

        var ticket = await GetTicketSnapshotAsync("1");
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
    }

    [Fact]
    public async Task Add_tags_appends_without_duplicating_and_keeps_existing()
    {
        var provider = await CreateProviderAsync([Ticket("1", tags: ["existing"])]);

        await provider.UpdateTicketAsync("1", new TicketUpdate
        {
            AddTags = ["existing", "fresh"],
        }, CancellationToken.None);

        var ticket = await GetTicketSnapshotAsync("1");
        Assert.Equal(["existing", "fresh"], ticket.Tags.ToArray());
    }

    [Fact]
    public async Task Set_custom_fields_upserts_only_the_named_fields()
    {
        var provider = await CreateProviderAsync(
        [
            Ticket("1", customFields: new Dictionary<string, string>
            {
                ["bookingCode"] = "MT-OLDVALUE",
                ["channel"] = "Wavehopper",
            }),
        ]);

        await provider.UpdateTicketAsync("1", new TicketUpdate
        {
            SetCustomFields = new Dictionary<string, string> { ["bookingCode"] = "MT-1A2B3C4D" },
        }, CancellationToken.None);

        var ticket = await GetTicketSnapshotAsync("1");
        Assert.Equal("MT-1A2B3C4D", ticket.CustomFields["bookingCode"]);
        Assert.Equal("Wavehopper", ticket.CustomFields["channel"]);
    }

    [Fact]
    public async Task Updating_an_unknown_ticket_throws_not_found()
    {
        var provider = await CreateProviderAsync([Ticket("1")]);

        await Assert.ThrowsAsync<HelpdeskTicketNotFoundException>(() =>
            provider.UpdateTicketAsync("missing", new TicketUpdate
            {
                Status = TicketStatus.Closed,
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Private_notes_are_stored_in_order()
    {
        var provider = await CreateProviderAsync([Ticket("1")]);

        await provider.AddPrivateNoteAsync("1", "first note", CancellationToken.None);
        await provider.AddPrivateNoteAsync("1", "second note", CancellationToken.None);

        var notes = await GetNotesAsync("1");
        Assert.Equal(["first note", "second note"], notes.ToArray());
    }
}
