using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Tenancy;

namespace Xenaia.Core.Tests.BusinessHours;

public class BusinessHoursServiceTests
{
    // Meridian Trails: Mon-Fri 09:00-17:00 New York time, closed 2026-01-01.
    private static BusinessHoursService Service(FakeTimeProvider? time = null)
    {
        var profile = new TenantProfileOptions
        {
            BusinessName = "Meridian Trails",
            TimeZone = "America/New_York",
            Locales = ["en-US"],
            BusinessHours = new BusinessHoursOptions
            {
                Weekly = Enum.GetValues<DayOfWeek>()
                    .Where(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    .Select(d => new DailyWindow { Day = d, Open = new TimeOnly(9, 0), Close = new TimeOnly(17, 0) })
                    .ToList(),
                Holidays = [new DateOnly(2026, 1, 1)],
            },
        };
        return new BusinessHoursService(
            Microsoft.Extensions.Options.Options.Create(profile),
            time ?? new FakeTimeProvider());
    }

    // 2026-01-05 is a Monday. 15:00 UTC == 10:00 in New York (UTC-5 in winter).
    private static readonly DateTimeOffset MondayMorningUtc =
        new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_during_a_weekday_window()
    {
        Assert.True(Service().IsOpenAt(MondayMorningUtc));
    }

    [Fact]
    public void Closed_before_opening_local_time()
    {
        // 12:00 UTC == 07:00 New York.
        var earlyUtc = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
        Assert.False(Service().IsOpenAt(earlyUtc));
    }

    [Fact]
    public void Closed_on_weekends()
    {
        // 2026-01-03 is a Saturday; 15:00 UTC is mid-morning in New York.
        var saturdayUtc = new DateTimeOffset(2026, 1, 3, 15, 0, 0, TimeSpan.Zero);
        Assert.False(Service().IsOpenAt(saturdayUtc));
    }

    [Fact]
    public void Closed_on_holidays()
    {
        // 2026-01-01 is a Thursday and a configured holiday.
        var holidayUtc = new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero);
        Assert.False(Service().IsOpenAt(holidayUtc));
    }

    [Fact]
    public void IsOpenNow_uses_the_injected_clock()
    {
        var clock = new FakeTimeProvider(MondayMorningUtc);
        Assert.True(Service(clock).IsOpenNow());
    }

    [Fact]
    public void Next_opening_after_a_saturday_is_monday_nine_local()
    {
        var saturdayUtc = new DateTimeOffset(2026, 1, 3, 15, 0, 0, TimeSpan.Zero);

        var next = Service().NextOpeningAfter(saturdayUtc);

        // Monday 2026-01-05 09:00 New York == 14:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 14, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Next_opening_skips_holidays()
    {
        // Wednesday 2025-12-31 18:00 New York (23:00 UTC): next window would be
        // Thursday Jan 1, but that is a holiday, so Friday Jan 2 09:00 local.
        var newYearsEveUtc = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);

        var next = Service().NextOpeningAfter(newYearsEveUtc);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 14, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void No_windows_configured_means_no_next_opening()
    {
        var profile = new TenantProfileOptions
        {
            BusinessName = "Meridian Trails",
            TimeZone = "America/New_York",
            Locales = ["en-US"],
        };
        var service = new BusinessHoursService(
            Microsoft.Extensions.Options.Options.Create(profile), new FakeTimeProvider());

        Assert.Null(service.NextOpeningAfter(MondayMorningUtc));
    }
}
