using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Xenaia.Core.Options;

public static class OptionsRegistration
{
    /// <summary>
    /// Binds TOptions from its declared section, validates data annotations
    /// plus any registered IValidateOptions, and fails the host at startup
    /// when invalid. A missing section throws immediately: absent tenant
    /// configuration is never silently defaulted (fail closed).
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration)
        where TOptions : class, ISectionOptions
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetRequiredSection(TOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}
