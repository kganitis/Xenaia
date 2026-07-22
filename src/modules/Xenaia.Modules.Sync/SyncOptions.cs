using Xenaia.Core.Options;

namespace Xenaia.Modules.Sync;

public sealed record SyncOptions : ISectionOptions
{
    public static string SectionName => "Sync";

    public FlowsOptions Flows { get; init; } = new();
    public AvailabilityOptions Availability { get; init; } = new();
    public BookingsOptions Bookings { get; init; } = new();
    public CatalogOptions Catalog { get; init; } = new();
    public RetryOptions Retry { get; init; } = new();

    /// <summary>Set by the host when a spreadsheet provider is configured
    /// (see Task 16); only then does the validator require Availability's
    /// PatchSheetName/GetSheetName to be non-blank. Mutable so the host can
    /// flip it through PostConfigure after binding.</summary>
    public bool RequireSheetNames { get; set; }
}

public sealed record FlowsOptions
{
    public bool AvailabilityOutbound { get; init; } = true;
    public bool AvailabilityInbound { get; init; } = true;
    public bool BookingsInbound { get; init; } = true;
    public bool BookingsOutbound { get; init; } = true;
    public bool Catalog { get; init; } = true;
}

public sealed record AvailabilityOptions
{
    public int MaxBatchSize { get; init; } = 1000;
    public int ChannelCapacity { get; init; } = 10000;
    public string PatchSheetName { get; init; } = "";
    public string GetSheetName { get; init; } = "";
    public int FetchDelayMs { get; init; } = 1000;
}

public sealed record BookingsOptions
{
    public int PollSeconds { get; init; } = 300;
    public int OverlapSeconds { get; init; } = 60;
    public int BackfillDays { get; init; } = 30;
    public int ChannelCapacity { get; init; } = 1000;
}

public sealed record CatalogOptions
{
    public string RefreshUtcTime { get; init; } = "03:00";
    public int FetchDelayMs { get; init; } = 1000;
}

public sealed record RetryOptions
{
    public int Attempts { get; init; } = 4;
    public int BaseDelaySeconds { get; init; } = 2;
}
