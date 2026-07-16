using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xenaia.Core.BusinessHours;
using Xenaia.Core.Events;
using Xenaia.Core.Notifications;
using Xenaia.Core.Options;
using Xenaia.Core.Tenancy;

namespace Xenaia.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared kernel. Hosts call this before any module's
    /// AddXxxServices. Fails immediately when the Tenant section is absent.
    /// </summary>
    public static IServiceCollection AddXenaiaCore(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<TenantProfileOptions>, TenantProfileValidator>();
        services.AddValidatedOptions<TenantProfileOptions>(configuration);
        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IBusinessHoursService, BusinessHoursService>();
        return services;
    }
}
