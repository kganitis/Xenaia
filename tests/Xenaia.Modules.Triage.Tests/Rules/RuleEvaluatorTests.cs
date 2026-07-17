using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Tests.Rules;

public class RuleEvaluatorTests
{
    private static readonly RuleEvaluator Evaluator = new(NullLogger<RuleEvaluator>.Instance);

    // $$""" so that regex quantifiers like {8} stay literal; only {{...}} interpolates.
    private static RulePack Pack(string rulesYaml) => RulePackLoader.Parse($$"""
        version: 1
        defaults:
          unmatchedCategory: needs-human
        rules:
        {{rulesYaml}}
        """);

    private static TicketFields Fields(
        string subject = "", string body = "", string sender = "", string channel = "") =>
        new(subject, body, sender, channel);

    [Fact]
    public void First_matching_rule_wins()
    {
        var pack = Pack("""
              - id: first
                category: first-category
                match: { subject: booking }
              - id: second
                category: second-category
                match: { subject: booking }
            """);

        var match = Evaluator.Evaluate(pack, Fields(subject: "New Booking"));

        Assert.NotNull(match);
        Assert.Equal("first", match!.Rule.Id);
    }

    [Fact]
    public void All_fields_must_match()
    {
        var pack = Pack("""
              - id: strict
                category: c
                match:
                  subject: booking
                  channel: wavehopper
            """);

        Assert.Null(Evaluator.Evaluate(pack, Fields(subject: "New Booking", channel: "TrailBooker")));
        Assert.NotNull(Evaluator.Evaluate(pack, Fields(subject: "New Booking", channel: "Wavehopper")));
    }

    [Fact]
    public void Any_pattern_in_a_field_list_may_match()
    {
        var pack = Pack("""
              - id: pay
                category: payment-received
                match:
                  subject:
                    - '^Payment received'
                    - '^ZahlFix: Zahlung erhalten'
            """);

        Assert.NotNull(Evaluator.Evaluate(pack, Fields(subject: "ZahlFix: Zahlung erhalten für Bestellung X")));
    }

    [Fact]
    public void No_matching_rule_returns_null()
    {
        var pack = Pack("""
              - id: pay
                category: payment-received
                match: { subject: '^Payment' }
            """);

        Assert.Null(Evaluator.Evaluate(pack, Fields(subject: "General question about kayaks")));
    }

    [Fact]
    public void Captures_come_from_match_and_extract_patterns()
    {
        var pack = Pack("""
              - id: new-booking
                category: new-booking
                match:
                  subject: '^New Booking\s+\[(?<bookingCode>MT-[A-Z0-9]{8})\]'
                extract:
                  productCode:
                    from: body
                    pattern: 'Product\s+Code\s+(?<productCode>MTP-[A-Z0-9]{4})'
            """);

        var match = Evaluator.Evaluate(pack, Fields(
            subject: "New Booking [MT-7Q2K9F4A]",
            body: "Product Code MTP-KAYA\nDate 25/12/2026"));

        Assert.NotNull(match);
        Assert.Equal("MT-7Q2K9F4A", match!.Captures["bookingCode"]);
        Assert.Equal("MTP-KAYA", match.Captures["productCode"]);
    }

    [Fact]
    public void Non_matching_extraction_yields_absent_capture_not_error()
    {
        var pack = Pack("""
              - id: new-booking
                category: new-booking
                match: { subject: '^New Booking' }
                extract:
                  productCode:
                    from: body
                    pattern: '(?<productCode>MTP-[A-Z0-9]{4})'
            """);

        var match = Evaluator.Evaluate(pack, Fields(subject: "New Booking", body: "no code here"));

        Assert.NotNull(match);
        Assert.False(match!.Captures.ContainsKey("productCode"));
    }

    [Fact]
    public void Sender_and_channel_fields_match_raw_values()
    {
        var pack = Pack("""
              - id: partner
                category: partner-mail
                match:
                  sender: '@trailbooker\.example$'
            """);

        Assert.NotNull(Evaluator.Evaluate(pack, Fields(sender: "ops@trailbooker.example")));
        Assert.Null(Evaluator.Evaluate(pack, Fields(sender: "guest@elsewhere.example")));
    }

    [Fact]
    public void Timeout_counts_as_no_match_and_falls_through()
    {
        var pack = Pack("""
              - id: hostile
                category: never
                match: { subject: '^(a+)+$' }
              - id: safety
                category: caught
                match: { subject: 'a' }
            """);

        var hostile = new string('a', 60) + "!";
        var match = Evaluator.Evaluate(pack, Fields(subject: hostile));

        Assert.NotNull(match);
        Assert.Equal("safety", match!.Rule.Id);
    }
}
