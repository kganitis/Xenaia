using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Adapters.BrightTide;

public static class BrightTideServiceCollectionExtensions
{
    /// <summary>Registers BrightTide as the IBookingSystemProvider. Hosts call
    /// this when Providers:BookingSystem is "brighttide".</summary>
    public static IServiceCollection AddBrightTideBookingSystem(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<BrightTideOptions>(configuration);
        services.AddSingleton<IValidateOptions<BrightTideOptions>, BrightTideOptionsValidator>();

        services.AddHttpClient<BrightTideClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BrightTideOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("API-Key", opts.ApiKey);
        }).AddStandardResilienceHandler();

        services.AddScoped<IBookingSystemProvider>(sp => sp.GetRequiredService<BrightTideClient>());
        return services;
    }
}
