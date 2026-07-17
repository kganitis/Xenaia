using Xunit;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Tests.Processing;

public class ActionFoldingTests
{
    private static readonly Dictionary<string, string> Captures = new()
    {
        ["bookingCode"] = "MT-7Q2K9F4A",
        ["amount"] = "84.50",
    };

    [Fact]
    public void Each_action_kind_lands_on_the_draft()
    {
        var draft = new TicketUpdateDraft();

        ActionFolder.Fold(
        [
            new SetStatusAction(TicketStatus.Resolved),
            new SetPriorityAction(TicketPriority.High),
            new AddTagsAction(["auto-triaged", "payment"]),
            new SetCustomFieldsAction(new Dictionary<string, string> { ["bookingCode"] = "{bookingCode}" }),
            new AddNoteAction("Payment of {amount} recorded."),
        ], Captures, draft);

        Assert.Equal(TicketStatus.Resolved, draft.Status);
        Assert.Equal(TicketPriority.High, draft.Priority);
        Assert.Equal(["auto-triaged", "payment"], draft.Tags.ToArray());
        Assert.Equal(["Payment of 84.50 recorded."], draft.Notes.ToArray());

        var update = draft.ToTicketUpdate();
        Assert.Equal("MT-7Q2K9F4A", update.SetCustomFields["bookingCode"]);
    }

    [Fact]
    public void Later_actions_win_on_conflict()
    {
        var draft = new TicketUpdateDraft();

        ActionFolder.Fold(
        [
            new SetStatusAction(TicketStatus.Resolved),
            new SetStatusAction(TicketStatus.Closed),
        ], Captures, draft);

        Assert.Equal(TicketStatus.Closed, draft.Status);
    }

    [Fact]
    public void Tags_do_not_duplicate()
    {
        var draft = new TicketUpdateDraft();

        ActionFolder.Fold(
        [
            new AddTagsAction(["auto-triaged"]),
            new AddTagsAction(["Auto-Triaged", "review"]),
        ], Captures, draft);

        Assert.Equal(["auto-triaged", "review"], draft.Tags.ToArray());
    }

    [Fact]
    public void Absent_capture_substitutes_empty_string()
    {
        Assert.Equal("code , done",
            TokenSubstitution.Substitute("code {missing}, done", Captures));
    }

    [Fact]
    public void Empty_draft_produces_a_no_op_update()
    {
        var update = new TicketUpdateDraft().ToTicketUpdate();

        Assert.Null(update.Status);
        Assert.Null(update.Priority);
        Assert.Empty(update.AddTags);
        Assert.Empty(update.SetCustomFields);
    }
}
