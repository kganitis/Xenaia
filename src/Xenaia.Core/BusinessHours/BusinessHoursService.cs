using Microsoft.Extensions.Options;
using Xenaia.Core.Tenancy;

namespace Xenaia.Core.BusinessHours;

/// <summary>
/// Evaluates the tenant's weekly windows and holidays in the tenant's
/// time zone. All instants come in and go out as DateTimeOffset (UTC-safe);
/// TimeProvider supplies "now" so tests control the clock.
/// </summary>
public sealed class BusinessHoursService(
    IOptions<TenantProfileOptions> tenant,
    TimeProvider timeProvider) : IBusinessHoursService
{
    public bool IsOpenNow() => IsOpenAt(timeProvider.GetUtcNow());

    public bool IsOpenAt(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TenantTimeZone());
        var date = DateOnly.FromDateTime(local.DateTime);

        if (Hours.Holidays.Contains(date)) return false;

        var window = WindowFor(local.DayOfWeek);
        if (window is null) return false;

        var time = TimeOnly.FromDateTime(local.DateTime);
        return time >= window.Open && time < window.Close;
    }

    public DateTimeOffset? NextOpeningAfter(DateTimeOffset instant)
    {
        if (Hours.Weekly.Count == 0) return null;

        var tz = TenantTimeZone();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);

        // 14 days covers any weekly pattern plus a holiday run.
        for (var offset = 0; offset <= 14; offset++)
        {
            var date = localDate.AddDays(offset);
            if (Hours.Holidays.Contains(date)) continue;

            var window = WindowFor(date.DayOfWeek);
            if (window is null) continue;

            // DST note: if Open falls in a skipped hour, GetUtcOffset resolves
            // to the standard offset; a one-hour skew on two nights a year is
            // acceptable for hold-message timing.
            var openLocal = date.ToDateTime(window.Open, DateTimeKind.Unspecified);
            var opening = new DateTimeOffset(openLocal, tz.GetUtcOffset(openLocal));
            if (opening > instant) return opening;
        }

        return null;
    }

    private BusinessHoursOptions Hours => tenant.Value.BusinessHours;

    private DailyWindow? WindowFor(DayOfWeek day) =>
        Hours.Weekly.FirstOrDefault(w => w.Day == day);

    private TimeZoneInfo TenantTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById(tenant.Value.TimeZone);
}
