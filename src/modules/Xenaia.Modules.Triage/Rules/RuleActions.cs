using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>One declarative action from a rule's actions list.</summary>
public abstract record RuleAction;

public sealed record SetStatusAction(TicketStatus Status) : RuleAction;

public sealed record SetPriorityAction(TicketPriority Priority) : RuleAction;

public sealed record AddTagsAction(IReadOnlyList<string> Tags) : RuleAction;

public sealed record SetCustomFieldsAction(IReadOnlyDictionary<string, string> Fields) : RuleAction;

public sealed record AddNoteAction(string Template) : RuleAction;
