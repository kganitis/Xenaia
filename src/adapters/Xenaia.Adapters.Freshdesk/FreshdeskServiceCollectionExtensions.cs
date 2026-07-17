using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Modules.Triage.Helpdesk;

namespace Xenaia.Adapters.Freshdesk;

public static class FreshdeskServiceCollectionExtensions
{
    /// <summary>Registers Freshdesk as the IHelpdeskProvider. Hosts call this
    /// when Providers:Helpdesk is "freshdesk".</summary>
    public static IServiceCollection AddFreshdeskHelpdesk(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<FreshdeskOptions>(configuration);
        services.AddHttpClient<IHelpdeskProvider, FreshdeskHelpdeskProvider>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FreshdeskOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddStandardResilienceHandler();
        return services;
    }
}
