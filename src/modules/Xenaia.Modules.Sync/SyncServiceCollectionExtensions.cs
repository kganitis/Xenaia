using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;

namespace Xenaia.Modules.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sync module: options + validator only, for now. Later
    /// tasks append flow services and hosted services to this one method as
    /// BrightTide and Google Workspace adapters come online.
    /// </summary>
    public static IServiceCollection AddSyncModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<SyncOptions>(configuration);
        services.AddSingleton<IValidateOptions<SyncOptions>, SyncOptionsValidator>();
        return services;
    }
}
