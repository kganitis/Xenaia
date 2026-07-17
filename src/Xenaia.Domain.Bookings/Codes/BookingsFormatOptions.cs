using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// Tenant-configured code grammars for the bookings domain. The product
/// ships no default patterns; a deployment without them does not start.
/// </summary>
public sealed record BookingsFormatOptions : ISectionOptions
{
    public static string SectionName => "Tenant:Bookings";

    [Required(AllowEmptyStrings = false)]
    public string BookingCodePattern { get; init; } = "";

    [Required(AllowEmptyStrings = false)]
    public string ProductCodePattern { get; init; } = "";
}
