using Xunit;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Tests.Rules;

public class RulePackLoaderTests
{
    private const string ValidPack = """
        version: 1
        defaults:
          unmatchedCategory: needs-human
        rules:
          - id: new-booking
            category: new-booking
            match:
              subject: '^New Booking\s+\[(?<bookingCode>MT-[A-Z0-9]{8})\]'
            extract:
              productCode:
                from: body
                pattern: 'Product\s+Code\s+(?<productCode>MTP-[A-Z0-9]{4})'
            actions:
              - setCustomFields: { bookingCode: '{bookingCode}' }
              - addTags: [auto-triaged]
              - setStatus: resolved
              - setPriority: high
              - addNote: 'Code {bookingCode}, product {productCode}.'
            processor: booking-urgency
          - id: fallback-review
            category: review-received
            match:
              subject:
                - 'new review'
                - 'fresh feedback'
        """;

    [Fact]
    public void Valid_pack_parses_completely()
    {
        var pack = RulePackLoader.Parse(ValidPack);

        Assert.Equal(1, pack.Version);
        Assert.Equal("needs-human", pack.UnmatchedCategory);
        Assert.Equal(2, pack.Rules.Count);

        var rule = pack.Rules[0];
        Assert.Equal("new-booking", rule.Id);
        Assert.Equal("new-booking", rule.Category);
        Assert.Equal("booking-urgency", rule.ProcessorName);
        Assert.Single(rule.Conditions);
        Assert.Equal(TicketField.Subject, rule.Conditions[0].Field);
        Assert.Single(rule.Extractions);
        Assert.Equal("productCode", rule.Extractions[0].Name);
        Assert.Equal(TicketField.Body, rule.Extractions[0].From);
        Assert.Equal(5, rule.Actions.Count);
        Assert.IsType<SetCustomFieldsAction>(rule.Actions[0]);
        Assert.IsType<AddTagsAction>(rule.Actions[1]);
        Assert.Equal(TicketStatus.Resolved, Assert.IsType<SetStatusAction>(rule.Actions[2]).Status);
        Assert.Equal(TicketPriority.High, Assert.IsType<SetPriorityAction>(rule.Actions[3]).Priority);
        Assert.IsType<AddNoteAction>(rule.Actions[4]);

        Assert.Equal(2, pack.Rules[1].Conditions[0].Patterns.Count);
        Assert.Null(pack.Rules[1].ProcessorName);
    }

    [Theory]
    [InlineData("version: 2\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }", "version must be 1")]
    [InlineData("version: 1\nrules:\n  - id: a\n    category: c\n    match: { subject: p }", "unmatchedCategory")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules: []", "non-empty list")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nsurprise: y\nrules:\n  - id: a\n    category: c\n    match: { subject: p }", "unknown key 'surprise'")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n  - id: a\n    category: d\n    match: { subject: q }", "Duplicate rule id 'a'")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    match: { subject: p }", "category is required")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c", "match is required")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { moon: p }", "unknown match field 'moon'")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: '(broken' }", "invalid regex")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    actions:\n      - explode: now", "unknown action 'explode'")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    actions:\n      - setStatus: vanished", "setStatus must be")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    actions:\n      - setPriority: apocalyptic", "setPriority must be")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    actions:\n      - addNote: 'hello {ghost}'", "token '{ghost}'")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    extract:\n      code:\n        from: channel\n        pattern: '(?<code>X)'", "from must be subject or body")]
    [InlineData("version: 1\ndefaults:\n  unmatchedCategory: x\nrules:\n  - id: a\n    category: c\n    match: { subject: p }\n    extract:\n      code:\n        from: body\n        pattern: '(?<other>X)'", "named capture 'code'")]
    public void Invalid_packs_fail_closed_with_a_specific_error(string yaml, string expectedFragment)
    {
        var ex = Assert.Throws<RulePackValidationException>(() => RulePackLoader.Parse(yaml));

        Assert.Contains(ex.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unparseable_yaml_fails_closed()
    {
        var ex = Assert.Throws<RulePackValidationException>(() => RulePackLoader.Parse("rules: ["));

        Assert.Contains(ex.Errors, e => e.Contains("not valid YAML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_file_fails_closed()
    {
        var ex = Assert.Throws<RulePackValidationException>(() =>
            RulePackLoader.Load("/nowhere/no-pack.yaml"));

        Assert.Contains(ex.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        const string yaml = """
            version: 3
            defaults:
              unmatchedCategory: ''
            rules:
              - id: a
                category: c
                match: { subject: '(broken' }
            """;

        var ex = Assert.Throws<RulePackValidationException>(() => RulePackLoader.Parse(yaml));

        Assert.True(ex.Errors.Count >= 3);
    }
}
