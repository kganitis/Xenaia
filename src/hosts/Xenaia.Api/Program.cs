using Xenaia.Adapters.Freshdesk;
using Xenaia.Core;
using Xenaia.Data;
using Xenaia.Data.PostgreSql;
using Xenaia.Domain.Bookings;
using Xenaia.Modules.Triage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddXenaiaCore(builder.Configuration);
builder.Services.AddBookingsDomain(builder.Configuration);
builder.Services.AddXenaiaPostgreSql(builder.Configuration);

var helpdeskProvider = builder.Configuration["Providers:Helpdesk"];
switch (helpdeskProvider)
{
    case null or "":
        break;
    case "freshdesk":
        builder.Services.AddTriageModule(builder.Configuration);
        builder.Services.AddFreshdeskHelpdesk(builder.Configuration);
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown helpdesk provider '{helpdeskProvider}'. Supported values: freshdesk.");
}

builder.Services.AddHealthChecks().AddDbContextCheck<XenaiaDbContext>();

var app = builder.Build();

if (string.IsNullOrEmpty(helpdeskProvider))
    app.Logger.LogInformation("Triage is disabled: no Providers:Helpdesk configured");

app.MapHealthChecks("/health");

app.Run();
