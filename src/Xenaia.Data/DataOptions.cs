using System.ComponentModel.DataAnnotations;
using Xenaia.Core.Options;

namespace Xenaia.Data;

/// <summary>Relational data-layer configuration. Fail closed: a host
/// without a Data section never starts.</summary>
public sealed record DataOptions : ISectionOptions
{
    public static string SectionName => "Data";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = "";

    /// <summary>Apply pending migrations at host startup (aborting startup
    /// on failure). Disable where migrations are applied out of band.</summary>
    public bool AutoMigrate { get; init; } = true;
}
