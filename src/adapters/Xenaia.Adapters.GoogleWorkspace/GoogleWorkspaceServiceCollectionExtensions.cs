using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.DependencyInjection;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Adapters.GoogleWorkspace;

public static class GoogleWorkspaceServiceCollectionExtensions
{
    private const string CredentialsJsonEnvVar = "GOOGLE_APPLICATION_CREDENTIALS_JSON";

    /// <summary>Registers GoogleWorkspace as the ISpreadsheetGateway. Hosts call
    /// this when Providers:Spreadsheet is "googleworkspace". Credential comes
    /// from the service-account JSON in the GOOGLE_APPLICATION_CREDENTIALS_JSON
    /// environment variable, falling back to Application Default Credentials,
    /// scoped to Sheets read/write. The SheetsService and the ISheetsApi seam
    /// over it are singletons; the gateway is scoped, matching the
    /// Freshdesk/BrightTide provider precedent.</summary>
    public static IServiceCollection AddGoogleWorkspaceSpreadsheets(this IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            var credential = CreateCredentialAsync().GetAwaiter().GetResult();
            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Xenaia",
            });
        });
        services.AddSingleton<ISheetsApi, GoogleSheetsApi>();
        services.AddScoped<ISpreadsheetGateway, GoogleSpreadsheetGateway>();
        return services;
    }

    private static async Task<GoogleCredential> CreateCredentialAsync()
    {
        var json = Environment.GetEnvironmentVariable(CredentialsJsonEnvVar);
        var credential = string.IsNullOrEmpty(json)
            ? await GoogleCredential.GetApplicationDefaultAsync()
            : GoogleCredential.FromJson(json);
        return credential.CreateScoped(SheetsService.Scope.Spreadsheets);
    }
}
