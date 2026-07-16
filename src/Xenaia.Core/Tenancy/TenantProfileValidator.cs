using System.Globalization;
using Microsoft.Extensions.Options;

namespace Xenaia.Core.Tenancy;

/// <summary>
/// Semantic validation beyond data annotations. Registered as
/// IValidateOptions so a bad tenant profile stops the host at startup.
/// </summary>
public sealed class TenantProfileValidator : IValidateOptions<TenantProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, TenantProfileOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TimeZone)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _))
            failures.Add($"Tenant:TimeZone '{options.TimeZone}' is not a known time zone id.");

        foreach (var locale in options.Locales)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                failures.Add("Tenant:Locales contains a null or empty entry.");
                continue;
            }

            try
            {
                _ = CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                failures.Add($"Tenant:Locales entry '{locale}' is not a known culture.");
            }
        }

        var duplicateDays = options.BusinessHours.Weekly
            .GroupBy(w => w.Day)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .ToList();
        if (duplicateDays.Count > 0)
            failures.Add($"Tenant:BusinessHours:Weekly has more than one window for: {string.Join(", ", duplicateDays)}.");

        foreach (var window in options.BusinessHours.Weekly.Where(w => w.Open >= w.Close))
            failures.Add($"Tenant:BusinessHours:Weekly window for {window.Day} closes at or before it opens.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
