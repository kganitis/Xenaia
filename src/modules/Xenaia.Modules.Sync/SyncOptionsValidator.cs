using Microsoft.Extensions.Options;

namespace Xenaia.Modules.Sync;

/// <summary>
/// Fail-closed startup gate for Sync options (model on TriageOptionsValidator).
/// Sheet names are required only when RequireSheetNames is true, set by the
/// host when a spreadsheet provider is configured.
/// </summary>
public sealed class SyncOptionsValidator : IValidateOptions<SyncOptions>
{
    public ValidateOptionsResult Validate(string? name, SyncOptions options)
    {
        var errors = new List<string>();

        if (options.Availability.MaxBatchSize <= 0)
            errors.Add("Sync:Availability:MaxBatchSize must be greater than 0.");
        if (options.Availability.ChannelCapacity <= 0)
            errors.Add("Sync:Availability:ChannelCapacity must be greater than 0.");
        if (options.Availability.FetchDelayMs < 0)
            errors.Add("Sync:Availability:FetchDelayMs must be zero or greater.");

        if (options.RequireSheetNames)
        {
            if (string.IsNullOrWhiteSpace(options.Availability.PatchSheetName))
                errors.Add("Sync:Availability:PatchSheetName is required when a spreadsheet provider is configured.");
            if (string.IsNullOrWhiteSpace(options.Availability.GetSheetName))
                errors.Add("Sync:Availability:GetSheetName is required when a spreadsheet provider is configured.");
        }

        if (options.Bookings.PollSeconds <= 0)
            errors.Add("Sync:Bookings:PollSeconds must be greater than 0.");
        if (options.Bookings.OverlapSeconds < 0)
            errors.Add("Sync:Bookings:OverlapSeconds must be zero or greater.");
        if (options.Bookings.BackfillDays <= 0)
            errors.Add("Sync:Bookings:BackfillDays must be greater than 0.");
        if (options.Bookings.ChannelCapacity <= 0)
            errors.Add("Sync:Bookings:ChannelCapacity must be greater than 0.");

        if (!TimeOnly.TryParse(options.Catalog.RefreshUtcTime, out _))
            errors.Add("Sync:Catalog:RefreshUtcTime must be a parseable time (e.g. '03:00').");
        if (options.Catalog.FetchDelayMs < 0)
            errors.Add("Sync:Catalog:FetchDelayMs must be zero or greater.");

        if (options.Retry.Attempts <= 0)
            errors.Add("Sync:Retry:Attempts must be greater than 0.");
        if (options.Retry.BaseDelaySeconds <= 0)
            errors.Add("Sync:Retry:BaseDelaySeconds must be greater than 0.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
