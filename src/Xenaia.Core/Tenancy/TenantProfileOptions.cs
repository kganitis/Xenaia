using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Core.Tenancy;

/// <summary>
/// The single business a deployment serves. Everything client-specific
/// flows from here; the product ships no defaults for any of it.
/// Records: validator tests derive invalid variants with `with`.
/// </summary>
public sealed record TenantProfileOptions : ISectionOptions
{
    public static string SectionName => "Tenant";

    [Required(AllowEmptyStrings = false)]
    public string BusinessName { get; init; } = "";

    /// <summary>IANA time zone id, e.g. "America/New_York".</summary>
    [Required(AllowEmptyStrings = false)]
    public string TimeZone { get; init; } = "";

    /// <summary>IETF language tags; the first entry is the default locale.</summary>
    [MinLength(1)]
    public List<string> Locales { get; init; } = [];

    public BusinessHoursOptions BusinessHours { get; init; } = new();
}

public sealed record BusinessHoursOptions
{
    /// <summary>At most one window per weekday; a missing day is closed.</summary>
    public List<DailyWindow> Weekly { get; init; } = [];

    public List<DateOnly> Holidays { get; init; } = [];
}

public sealed record DailyWindow
{
    public DayOfWeek Day { get; init; }
    public TimeOnly Open { get; init; }
    public TimeOnly Close { get; init; }
}
