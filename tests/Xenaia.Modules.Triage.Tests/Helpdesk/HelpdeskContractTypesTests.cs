using Xunit;
using Xenaia.Modules.Triage;
using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Modules.Triage.Tests.Helpdesk;

public class HelpdeskContractTypesTests
{
    [Fact]
    public void Ticket_defaults_are_empty_not_null()
    {
        var ticket = new HelpdeskTicket { Id = "1" };

        Assert.Equal("", ticket.Subject);
        Assert.Equal("", ticket.BodyHtml);
        Assert.Equal("", ticket.Sender);
        Assert.Equal("", ticket.Channel);
        Assert.Empty(ticket.Tags);
        Assert.Empty(ticket.CustomFields);
    }

    [Fact]
    public void Update_defaults_change_nothing()
    {
        var update = new TicketUpdate();

        Assert.Null(update.Status);
        Assert.Null(update.Priority);
        Assert.Empty(update.AddTags);
        Assert.Empty(update.SetCustomFields);
    }

    [Fact]
    public void Not_found_exception_carries_the_ticket_id()
    {
        var ex = new HelpdeskTicketNotFoundException("42");

        Assert.Equal("42", ex.TicketId);
        Assert.Contains("42", ex.Message);
    }

    [Fact]
    public void Marker_tag_is_the_specified_constant()
    {
        Assert.Equal("xenaia-triaged", TriageConstants.MarkerTag);
    }
}
