namespace Xenaia.Modules.Triage.Rules;

public sealed record RuleCondition(TicketField Field, IReadOnlyList<TimedRegex> Patterns);

public sealed record RuleExtraction(string Name, TicketField From, TimedRegex Pattern);

public sealed record TriageRule(
    string Id,
    string Category,
    IReadOnlyList<RuleCondition> Conditions,
    IReadOnlyList<RuleExtraction> Extractions,
    IReadOnlyList<RuleAction> Actions,
    string? ProcessorName);

public sealed record RulePack(
    int Version,
    string UnmatchedCategory,
    IReadOnlyList<TriageRule> Rules);
