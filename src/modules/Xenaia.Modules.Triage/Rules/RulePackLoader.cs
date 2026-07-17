using System.Text.RegularExpressions;
using Xenaia.Modules.Triage.Helpdesk;
using YamlDotNet.Serialization;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>
/// Loads and validates a YAML rule pack. Fail closed: every structural,
/// regex, or reference problem is collected and thrown together as one
/// RulePackValidationException, so a misconfigured deployment never
/// half-runs. Processor-name validation happens in TriageOptionsValidator,
/// which knows the registered processors.
/// </summary>
public static class RulePackLoader
{
    private static readonly Regex Token = new(
        @"\{([A-Za-z][A-Za-z0-9]*)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static RulePack Load(string path)
    {
        if (!File.Exists(path))
            throw new RulePackValidationException([$"Rule pack file not found: {path}"]);
        return Parse(File.ReadAllText(path));
    }

    public static RulePack Parse(string yaml)
    {
        object? root;
        try
        {
            root = new DeserializerBuilder().Build().Deserialize<object>(yaml);
        }
        catch (Exception ex)
        {
            throw new RulePackValidationException([$"Rule pack is not valid YAML: {ex.Message}"]);
        }

        if (root is not IDictionary<object, object> map)
            throw new RulePackValidationException(["Rule pack root must be a mapping."]);

        var errors = new List<string>();
        CheckKeys(map, "rule pack", ["version", "defaults", "rules"], errors);

        if (GetScalar(map, "version") != "1")
            errors.Add("version must be 1.");

        var unmatchedCategory = "";
        if (map.TryGetValue("defaults", out var defaultsValue))
        {
            if (defaultsValue is IDictionary<object, object> defaults)
            {
                CheckKeys(defaults, "defaults", ["unmatchedCategory"], errors);
                unmatchedCategory = GetScalar(defaults, "unmatchedCategory") ?? "";
            }
            else
            {
                errors.Add("defaults must be a mapping.");
            }
        }
        if (string.IsNullOrWhiteSpace(unmatchedCategory))
            errors.Add("defaults.unmatchedCategory is required and must be non-empty.");

        var rules = new List<TriageRule>();
        if (!map.TryGetValue("rules", out var rulesValue)
            || rulesValue is not IList<object> ruleList
            || ruleList.Count == 0)
        {
            errors.Add("rules must be a non-empty list.");
        }
        else
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ruleList.Count; i++)
            {
                var rule = ParseRule(ruleList[i], i, errors);
                if (rule is null) continue;
                if (!seenIds.Add(rule.Id))
                    errors.Add($"Duplicate rule id '{rule.Id}'.");
                rules.Add(rule);
            }
        }

        if (errors.Count > 0)
            throw new RulePackValidationException(errors);

        return new RulePack(1, unmatchedCategory, rules);
    }

    private static TriageRule? ParseRule(object item, int index, List<string> errors)
    {
        if (item is not IDictionary<object, object> map)
        {
            errors.Add($"rules[{index}] must be a mapping.");
            return null;
        }

        var id = GetScalar(map, "id");
        var where = string.IsNullOrWhiteSpace(id) ? $"rules[{index}]" : $"rule '{id}'";
        if (string.IsNullOrWhiteSpace(id))
            errors.Add($"rules[{index}]: id is required.");

        CheckKeys(map, where, ["id", "category", "match", "extract", "actions", "processor"], errors);

        var category = GetScalar(map, "category");
        if (string.IsNullOrWhiteSpace(category))
            errors.Add($"{where}: category is required and must be non-empty.");

        var conditions = ParseMatch(map, where, errors);
        var extractions = ParseExtract(map, where, errors);
        var actions = ParseActions(map, where, errors);
        var processor = GetScalar(map, "processor");

        var captureNames = conditions.SelectMany(c => c.Patterns)
            .Concat(extractions.Select(e => e.Pattern))
            .SelectMany(p => p.CaptureNames)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var template in TemplatesOf(actions))
        {
            foreach (Match token in Token.Matches(template))
            {
                var name = token.Groups[1].Value;
                if (!captureNames.Contains(name))
                    errors.Add($"{where}: token '{{{name}}}' does not reference a capture defined by this rule's patterns.");
            }
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(category))
            return null;
        return new TriageRule(
            id, category, conditions, extractions, actions,
            string.IsNullOrWhiteSpace(processor) ? null : processor);
    }

    private static List<RuleCondition> ParseMatch(
        IDictionary<object, object> rule, string where, List<string> errors)
    {
        var conditions = new List<RuleCondition>();
        if (!rule.TryGetValue("match", out var matchValue)
            || matchValue is not IDictionary<object, object> match
            || match.Count == 0)
        {
            errors.Add($"{where}: match is required and must be a non-empty mapping.");
            return conditions;
        }

        foreach (var (key, value) in match)
        {
            var fieldName = key.ToString() ?? "";
            if (!Enum.TryParse<TicketField>(fieldName, ignoreCase: true, out var field))
            {
                errors.Add($"{where}: unknown match field '{fieldName}' (expected subject, body, sender, or channel).");
                continue;
            }

            var rawPatterns = value switch
            {
                string single => new List<string> { single },
                IList<object> list => list.Select(p => p?.ToString() ?? "").ToList(),
                _ => new List<string>(),
            };
            if (rawPatterns.Count == 0 || rawPatterns.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{where}: match.{fieldName} must be a pattern or a non-empty list of patterns.");
                continue;
            }

            var patterns = new List<TimedRegex>();
            foreach (var raw in rawPatterns)
            {
                var compiled = TryCompile(raw, $"{where}: match.{fieldName}", errors);
                if (compiled is not null) patterns.Add(compiled);
            }
            if (patterns.Count == rawPatterns.Count)
                conditions.Add(new RuleCondition(field, patterns));
        }
        return conditions;
    }

    private static List<RuleExtraction> ParseExtract(
        IDictionary<object, object> rule, string where, List<string> errors)
    {
        var extractions = new List<RuleExtraction>();
        if (!rule.TryGetValue("extract", out var extractValue))
            return extractions;
        if (extractValue is not IDictionary<object, object> extract)
        {
            errors.Add($"{where}: extract must be a mapping.");
            return extractions;
        }

        foreach (var (key, value) in extract)
        {
            var name = key.ToString() ?? "";
            if (value is not IDictionary<object, object> entry)
            {
                errors.Add($"{where}: extract.{name} must be a mapping with from and pattern.");
                continue;
            }
            CheckKeys(entry, $"{where}: extract.{name}", ["from", "pattern"], errors);

            var from = GetScalar(entry, "from") ?? "";
            if (!Enum.TryParse<TicketField>(from, ignoreCase: true, out var field)
                || field is not (TicketField.Subject or TicketField.Body))
            {
                errors.Add($"{where}: extract.{name}.from must be subject or body.");
                continue;
            }

            var pattern = TryCompile(GetScalar(entry, "pattern") ?? "", $"{where}: extract.{name}", errors);
            if (pattern is null) continue;

            if (!pattern.CaptureNames.Contains(name, StringComparer.Ordinal))
            {
                errors.Add($"{where}: extract.{name} pattern must define a named capture '{name}'.");
                continue;
            }
            extractions.Add(new RuleExtraction(name, field, pattern));
        }
        return extractions;
    }

    private static List<RuleAction> ParseActions(
        IDictionary<object, object> rule, string where, List<string> errors)
    {
        var actions = new List<RuleAction>();
        if (!rule.TryGetValue("actions", out var actionsValue))
            return actions;
        if (actionsValue is not IList<object> list)
        {
            errors.Add($"{where}: actions must be a list.");
            return actions;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not IDictionary<object, object> entry || entry.Count != 1)
            {
                errors.Add($"{where}: actions[{i}] must be a mapping with exactly one action key.");
                continue;
            }
            var key = entry.Keys.First().ToString() ?? "";
            var action = ParseAction(key, entry.Values.First(), $"{where}: actions[{i}]", errors);
            if (action is not null) actions.Add(action);
        }
        return actions;
    }

    private static RuleAction? ParseAction(string key, object? value, string where, List<string> errors)
    {
        switch (key)
        {
            case "setStatus":
                if (Enum.TryParse<TicketStatus>(value?.ToString(), ignoreCase: true, out var status))
                    return new SetStatusAction(status);
                errors.Add($"{where}: setStatus must be open, pending, resolved, or closed.");
                return null;
            case "setPriority":
                if (Enum.TryParse<TicketPriority>(value?.ToString(), ignoreCase: true, out var priority))
                    return new SetPriorityAction(priority);
                errors.Add($"{where}: setPriority must be low, medium, high, or urgent.");
                return null;
            case "addTags":
                if (value is IList<object> tags && tags.Count > 0)
                {
                    var parsed = tags.Select(t => t?.ToString() ?? "").Where(t => t.Length > 0).ToList();
                    if (parsed.Count == tags.Count)
                        return new AddTagsAction(parsed);
                }
                errors.Add($"{where}: addTags must be a non-empty list of tags.");
                return null;
            case "setCustomFields":
                if (value is IDictionary<object, object> fields && fields.Count > 0)
                {
                    return new SetCustomFieldsAction(fields.ToDictionary(
                        kv => kv.Key.ToString() ?? "",
                        kv => kv.Value?.ToString() ?? "",
                        StringComparer.Ordinal));
                }
                errors.Add($"{where}: setCustomFields must be a non-empty mapping.");
                return null;
            case "addNote":
                var template = value?.ToString();
                if (!string.IsNullOrWhiteSpace(template))
                    return new AddNoteAction(template);
                errors.Add($"{where}: addNote must be a non-empty string.");
                return null;
            default:
                errors.Add($"{where}: unknown action '{key}'.");
                return null;
        }
    }

    private static void CheckKeys(
        IDictionary<object, object> map, string where, string[] known, List<string> errors)
    {
        foreach (var key in map.Keys.Select(k => k.ToString() ?? ""))
        {
            if (!known.Contains(key, StringComparer.Ordinal))
                errors.Add($"{where}: unknown key '{key}'.");
        }
    }

    private static string? GetScalar(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static TimedRegex? TryCompile(string pattern, string where, List<string> errors)
    {
        try
        {
            return TimedRegex.Create(pattern);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{where}: invalid regex '{pattern}': {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> TemplatesOf(List<RuleAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case SetCustomFieldsAction fieldsAction:
                    foreach (var value in fieldsAction.Fields.Values)
                        yield return value;
                    break;
                case AddNoteAction noteAction:
                    yield return noteAction.Template;
                    break;
            }
        }
    }
}
