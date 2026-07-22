namespace Xenaia.Modules.Sync.Tests;

public class SyncOptionsValidatorTests
{
    private static readonly SyncOptionsValidator Validator = new();

    [Fact]
    public void Defaults_are_valid_when_sheet_names_are_not_required()
    {
        var options = new SyncOptions { RequireSheetNames = false };

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Zero_max_batch_size_is_invalid()
    {
        var options = new SyncOptions
        {
            Availability = new AvailabilityOptions { MaxBatchSize = 0 },
        };

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Negative_fetch_delay_is_invalid()
    {
        var options = new SyncOptions
        {
            Availability = new AvailabilityOptions { FetchDelayMs = -1 },
        };

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Unparsable_refresh_time_is_invalid()
    {
        var options = new SyncOptions
        {
            Catalog = new CatalogOptions { RefreshUtcTime = "not-a-time" },
        };

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Blank_patch_sheet_name_is_invalid_when_sheet_names_are_required()
    {
        var options = new SyncOptions
        {
            RequireSheetNames = true,
            Availability = new AvailabilityOptions { PatchSheetName = "", GetSheetName = "Bookings" },
        };

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }
}
