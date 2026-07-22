using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Adapters.BrightTide;

/// <summary>
/// Bound from configuration section "BrightTide". BaseUrl and ApiKey are
/// required; the fail-closed <see cref="BrightTideOptionsValidator"/> enforces
/// that BaseUrl is an absolute HTTPS URL (localhost may be plain HTTP for local
/// development).
/// </summary>
public sealed class BrightTideOptions : ISectionOptions
{
    public static string SectionName => "BrightTide";

    /// <summary>e.g. https://api.brighttide.example/v1/</summary>
    [Required]
    public string BaseUrl { get; init; } = "";

    [Required]
    public string ApiKey { get; init; } = "";
}
