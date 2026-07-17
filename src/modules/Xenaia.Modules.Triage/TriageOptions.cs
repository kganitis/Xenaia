using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Modules.Triage;

public sealed class TriageOptions : ISectionOptions
{
    public static string SectionName => "Tenant:Triage";

    [Required]
    public string RulePackPath { get; init; } = "";

    [Range(5, 3600)]
    public int PollIntervalSeconds { get; init; } = 60;

    public UrgencyOptions Urgency { get; init; } = new();
}

public sealed class UrgencyOptions
{
    [Range(1, 168)]
    public int ProximityHours { get; init; } = 5;

    /// <summary>Formats tried in order when parsing the extracted booking
    /// date/time, interpreted in the tenant timezone.</summary>
    public List<string> DateTimeFormats { get; init; } = ["dd/MM/yyyy HH:mm"];
}
