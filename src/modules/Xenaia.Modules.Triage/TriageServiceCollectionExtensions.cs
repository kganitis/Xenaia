using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage;

public static class TriageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Triage module. The host also registers an
    /// IHelpdeskProvider adapter; without one the polling service cannot
    /// resolve and the host fails at startup, by design.
    ///
    /// <paramref name="bookingSystemConfigured"/> gates BookingLookupProcessor:
    /// its dependencies (IBookingStore, IBookingSystemProvider) only resolve
    /// when the host has a booking system wired up, so the host passes true
    /// only then. A rule pack naming "booking-lookup" without it fails
    /// startup via TriageOptionsValidator's unregistered-processor check.
    /// </summary>
    public static IServiceCollection AddTriageModule(
        this IServiceCollection services,
        IConfiguration configuration,
        bool bookingSystemConfigured = false)
    {
        services.AddValidatedOptions<TriageOptions>(configuration);
        services.AddSingleton<IValidateOptions<TriageOptions>, TriageOptionsValidator>();
        services.AddSingleton<IRulePackProvider, RulePackProvider>();
        services.AddSingleton<RuleEvaluator>();
        services.AddSingleton<ITicketProcessor, BookingUrgencyProcessor>();
        if (bookingSystemConfigured)
            services.AddScoped<ITicketProcessor, BookingLookupProcessor>();
        services.AddScoped<TriageSweep>();
        services.AddHostedService<TriagePollingService>();
        return services;
    }
}
