using Microsoft.Extensions.Logging.Abstractions;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;
using Xunit;

namespace Xenaia.Modules.Triage.Tests.Rules;

/// <summary>
/// Golden tests: the shipped sample pack against the fixture corpus. These
/// double as living documentation of the rule format; if a fixture's
/// expected outcome changes, the pack's meaning changed.
/// </summary>
public class MeridianTrailsGoldenTests
{
    private static readonly RulePack Pack = RulePackLoader.Load(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "meridian-trails.yaml"));

    private static readonly RuleEvaluator Evaluator = new(NullLogger<RuleEvaluator>.Instance);

    private static (TriageMatch? Match, TicketUpdateDraft Draft) Triage(string subject, string fixture)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var fields = new TicketFields(subject, TextNormalizer.Normalize(html), "", "");
        var match = Evaluator.Evaluate(Pack, fields);
        var draft = new TicketUpdateDraft();
        if (match is not null)
            ActionFolder.Fold(match.Rule.Actions, match.Captures, draft);
        return (match, draft);
    }

    [Fact]
    public void New_booking_extracts_codes_and_schedule()
    {
        var (match, draft) = Triage("New Booking [MT-7Q2K9F4A]", "new-booking-standard.html");

        Assert.NotNull(match);
        Assert.Equal("new-booking", match!.Rule.Category);
        Assert.Equal("booking-urgency", match.Rule.ProcessorName);
        Assert.Equal("MT-7Q2K9F4A", match.Captures["bookingCode"]);
        Assert.Equal("MTP-KAYA", match.Captures["productCode"]);
        Assert.Equal("25/12/2026", match.Captures["bookingDateTime"]);
        Assert.Equal("14:00", match.Captures["time"]);
        Assert.Equal(["auto-triaged"], draft.Tags.ToArray());
        Assert.Equal("MT-7Q2K9F4A", draft.ToTicketUpdate().SetCustomFields["bookingCode"]);
        Assert.Null(draft.Status);
    }

    [Fact]
    public void New_booking_without_product_code_still_categorizes()
    {
        var (match, _) = Triage("New Booking [MT-2B8D0E6F]", "new-booking-no-product-code.html");

        Assert.NotNull(match);
        Assert.Equal("new-booking", match!.Rule.Category);
        Assert.False(match.Captures.ContainsKey("productCode"));
        Assert.Equal("18:00", match.Captures["time"]);
    }

    [Fact]
    public void Cancellation_resolves_and_stamps_the_code()
    {
        var (match, draft) = Triage("Booking Cancelled [MT-7Q2K9F4A]", "booking-cancelled.html");

        Assert.Equal("booking-cancelled", match!.Rule.Category);
        Assert.Equal(TicketStatus.Resolved, draft.Status);
        Assert.Equal("MT-7Q2K9F4A", draft.ToTicketUpdate().SetCustomFields["bookingCode"]);
    }

    [Fact]
    public void English_payment_notification_notes_the_amount()
    {
        var (match, draft) = Triage(
            "Payment received for order WH-20261225-014", "payment-english.html");

        Assert.Equal("payment-received", match!.Rule.Category);
        Assert.Equal(
            ["Automated triage: payment of 84.50 EUR received for order WH-20261225-014."],
            draft.Notes.ToArray());
        Assert.Equal(["payment"], draft.Tags.ToArray());
    }

    [Fact]
    public void German_payment_notification_matches_the_locale_pattern()
    {
        var (match, draft) = Triage(
            "ZahlFix: Zahlung erhalten für Bestellung WH-20261225-015", "payment-german.html");

        Assert.Equal("payment-received", match!.Rule.Category);
        Assert.Equal(
            ["Automated triage: payment of 120,00 EUR received for order WH-20261225-015."],
            draft.Notes.ToArray());
    }

    [Fact]
    public void Review_notification_closes_tagged()
    {
        var (match, draft) = Triage("You have a new review on TrailBooker", "review.html");

        Assert.Equal("review-received", match!.Rule.Category);
        Assert.Equal(TicketStatus.Closed, draft.Status);
        Assert.Equal(["review"], draft.Tags.ToArray());
    }

    [Fact]
    public void Future_booking_categorizes_without_status_change()
    {
        var (match, draft) = Triage("New Booking [MT-9G5H3J1K]", "new-booking-future.html");

        Assert.Equal("new-booking", match!.Rule.Category);
        Assert.Equal("MTP-COOK", match.Captures["productCode"]);
        Assert.Equal("20/08/2026", match.Captures["bookingDateTime"]);
        Assert.Null(draft.Status);
        Assert.Null(draft.Priority);
    }

    [Fact]
    public void Usd_payment_notes_the_dollar_amount()
    {
        var (match, draft) = Triage(
            "Payment received for order TB-20260901-002", "payment-usd.html");

        Assert.Equal("payment-received", match!.Rule.Category);
        Assert.Equal(
            ["Automated triage: payment of 45.00 USD received for order TB-20260901-002."],
            draft.Notes.ToArray());
    }

    [Fact]
    public void General_question_matches_nothing()
    {
        var (match, _) = Triage("Do you allow dogs on kayaks?", "unmatched-question.html");

        Assert.Null(match);
        Assert.Equal("needs-human", Pack.UnmatchedCategory);
    }

    [Fact]
    public void Malformed_booking_code_falls_through_to_needs_human()
    {
        var (match, _) = Triage("New Booking [XX-12]", "new-booking-bad-code.html");

        Assert.Null(match);
    }
}
