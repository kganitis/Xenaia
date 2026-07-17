using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Adapters.Freshdesk;

public sealed class FreshdeskOptions : ISectionOptions
{
    public static string SectionName => "Adapters:Freshdesk";

    /// <summary>e.g. https://yoursubdomain.freshdesk.com/api/v2/</summary>
    [Required, Url]
    public string BaseUrl { get; init; } = "";

    [Required]
    public string ApiKey { get; init; } = "";

    /// <summary>Canonical field name -> vendor custom-field name (cf_*).</summary>
    public Dictionary<string, string> FieldMap { get; init; } = new();

    [Range(1, 100)]
    public int PageSize { get; init; } = 30;
}
