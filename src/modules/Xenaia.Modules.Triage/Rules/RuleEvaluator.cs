using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>The four normalized ticket fields rules match against.</summary>
public sealed record TicketFields(string Subject, string BodyText, string Sender, string Channel)
{
    public string Get(TicketField field) => field switch
    {
        TicketField.Subject => Subject,
        TicketField.Body => BodyText,
        TicketField.Sender => Sender,
        TicketField.Channel => Channel,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}

public sealed record TriageMatch(TriageRule Rule, IReadOnlyDictionary<string, string> Captures);

/// <summary>
/// Evaluates a rule pack against one ticket: rules in order, first match
/// wins, AND across fields, OR within a field's pattern list. Captures come
/// from every successful match and extraction pattern.
/// </summary>
public sealed class RuleEvaluator(ILogger<RuleEvaluator> logger)
{
    public TriageMatch? Evaluate(RulePack pack, TicketFields fields)
    {
        foreach (var rule in pack.Rules)
        {
            var captures = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!AllConditionsMatch(rule, fields, captures))
                continue;
            RunExtractions(rule, fields, captures);
            return new TriageMatch(rule, captures);
        }
        return null;
    }

    private bool AllConditionsMatch(
        TriageRule rule, TicketFields fields, Dictionary<string, string> captures)
    {
        foreach (var condition in rule.Conditions)
        {
            var input = fields.Get(condition.Field);
            Match? match = null;
            foreach (var pattern in condition.Patterns)
            {
                match = pattern.Match(input, out var timedOut);
                if (timedOut)
                {
                    logger.LogWarning(
                        "Rule {RuleId}: pattern on {Field} timed out; treated as no match",
                        rule.Id, condition.Field);
                }
                if (match is not null) break;
            }
            if (match is null) return false;
            CollectCaptures(match, captures);
        }
        return true;
    }

    private void RunExtractions(
        TriageRule rule, TicketFields fields, Dictionary<string, string> captures)
    {
        foreach (var extraction in rule.Extractions)
        {
            var match = extraction.Pattern.Match(fields.Get(extraction.From), out var timedOut);
            if (timedOut)
            {
                logger.LogWarning(
                    "Rule {RuleId}: extraction {Name} timed out; captures absent",
                    rule.Id, extraction.Name);
            }
            if (match is not null)
                CollectCaptures(match, captures);
        }
    }

    private static void CollectCaptures(Match match, Dictionary<string, string> captures)
    {
        foreach (Group group in match.Groups)
        {
            if (int.TryParse(group.Name, out _) || !group.Success)
                continue;
            captures[group.Name] = group.Value;
        }
    }
}
