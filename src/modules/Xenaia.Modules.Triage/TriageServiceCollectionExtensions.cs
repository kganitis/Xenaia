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
    /// </summary>
    public static IServiceCollection AddTriageModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<TriageOptions>(configuration);
        services.AddSingleton<IValidateOptions<TriageOptions>, TriageOptionsValidator>();
        services.AddSingleton<IRulePackProvider, RulePackProvider>();
        services.AddSingleton<RuleEvaluator>();
        services.AddSingleton<ITicketProcessor, BookingUrgencyProcessor>();
        return services;
    }
}
