using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Modules.Triage.Processing;

/// <summary>
/// The mutation being assembled for one ticket during a sweep. Declarative
/// actions fold into it first; a bound coded processor may amend it; the
/// pipeline then issues it as a single TicketUpdate.
/// </summary>
public sealed class TicketUpdateDraft
{
    private readonly List<string> _tags = [];
    private readonly Dictionary<string, string> _customFields = new(StringComparer.Ordinal);
    private readonly List<string> _notes = [];

    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public IReadOnlyList<string> Tags => _tags;
    public IReadOnlyList<string> Notes => _notes;

    public void AddTag(string tag)
    {
        if (!_tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            _tags.Add(tag);
    }

    public void SetCustomField(string name, string value) => _customFields[name] = value;

    public void AddNote(string body) => _notes.Add(body);

    public TicketUpdate ToTicketUpdate() => new()
    {
        Status = Status,
        Priority = Priority,
        AddTags = _tags.ToList(),
        SetCustomFields = new Dictionary<string, string>(_customFields),
    };
}
