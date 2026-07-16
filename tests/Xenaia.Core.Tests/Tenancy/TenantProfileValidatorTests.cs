using Xenaia.Core.Tenancy;

namespace Xenaia.Core.Tests.Tenancy;

public class TenantProfileValidatorTests
{
    private static TenantProfileOptions ValidProfile() => new()
    {
        BusinessName = "Meridian Trails",
        TimeZone = "America/New_York",
        Locales = ["en-US"],
        BusinessHours = new BusinessHoursOptions
        {
            Weekly =
            [
                new DailyWindow { Day = DayOfWeek.Monday, Open = new TimeOnly(9, 0), Close = new TimeOnly(17, 0) },
            ],
        },
    };

    private static readonly TenantProfileValidator Validator = new();

    [Fact]
    public void Accepts_a_valid_profile()
    {
        Assert.True(Validator.Validate(null, ValidProfile()).Succeeded);
    }

    [Fact]
    public void Rejects_an_unknown_time_zone()
    {
        var profile = ValidProfile();
        var result = Validator.Validate(null, profile with { TimeZone = "Nowhere/Imaginary" });

        Assert.True(result.Failed);
        Assert.Contains("TimeZone", result.FailureMessage);
    }

    [Fact]
    public void Rejects_an_unknown_locale()
    {
        var result = Validator.Validate(null, ValidProfile() with { Locales = ["xx-XX"] });

        Assert.True(result.Failed);
        Assert.Contains("Locales", result.FailureMessage);
    }

    [Fact]
    public void Rejects_duplicate_weekday_windows()
    {
        var profile = ValidProfile() with
        {
            BusinessHours = new BusinessHoursOptions
            {
                Weekly =
                [
                    new DailyWindow { Day = DayOfWeek.Monday, Open = new TimeOnly(9, 0), Close = new TimeOnly(12, 0) },
                    new DailyWindow { Day = DayOfWeek.Monday, Open = new TimeOnly(13, 0), Close = new TimeOnly(17, 0) },
                ],
            },
        };

        Assert.True(Validator.Validate(null, profile).Failed);
    }

    [Fact]
    public void Rejects_a_window_that_closes_before_it_opens()
    {
        var profile = ValidProfile() with
        {
            BusinessHours = new BusinessHoursOptions
            {
                Weekly =
                [
                    new DailyWindow { Day = DayOfWeek.Monday, Open = new TimeOnly(17, 0), Close = new TimeOnly(9, 0) },
                ],
            },
        };

        Assert.True(Validator.Validate(null, profile).Failed);
    }

    [Fact]
    public void Null_time_zone_fails_cleanly_instead_of_throwing()
    {
        var result = Validator.Validate(null, ValidProfile() with { TimeZone = null! });

        Assert.True(result.Failed);
        Assert.Contains("TimeZone", result.FailureMessage);
    }

    [Fact]
    public void Null_locale_entry_fails_cleanly_instead_of_throwing()
    {
        var result = Validator.Validate(null, ValidProfile() with { Locales = [null!] });

        Assert.True(result.Failed);
        Assert.Contains("Locales", result.FailureMessage);
    }
}
