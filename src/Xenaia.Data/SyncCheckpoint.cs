namespace Xenaia.Data;

/// <summary>
/// Infrastructure row for a named sync checkpoint (a "last pulled at"
/// watermark). Not a domain type: it carries no behaviour, has no invariants,
/// and never leaves the data layer. Keyed by <see cref="Name"/>.
/// </summary>
public sealed class SyncCheckpoint
{
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset Value { get; set; }
}
