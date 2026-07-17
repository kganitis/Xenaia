using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage.Processing;

public static class ActionFolder
{
    /// <summary>Applies a rule's declarative actions to the draft in order;
    /// later actions win on conflict.</summary>
    public static void Fold(
        IReadOnlyList<RuleAction> actions,
        IReadOnlyDictionary<string, string> captures,
        TicketUpdateDraft draft)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case SetStatusAction setStatus:
                    draft.Status = setStatus.Status;
                    break;
                case SetPriorityAction setPriority:
                    draft.Priority = setPriority.Priority;
                    break;
                case AddTagsAction addTags:
                    foreach (var tag in addTags.Tags)
                        draft.AddTag(tag);
                    break;
                case SetCustomFieldsAction setFields:
                    foreach (var (name, value) in setFields.Fields)
                        draft.SetCustomField(name, TokenSubstitution.Substitute(value, captures));
                    break;
                case AddNoteAction addNote:
                    draft.AddNote(TokenSubstitution.Substitute(addNote.Template, captures));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown action type {action.GetType().Name}.");
            }
        }
    }
}
