using Xenaia.Adapters.BrightTide;
using Xenaia.Adapters.Freshdesk;
using Xenaia.Adapters.GoogleWorkspace;
using Xenaia.Api;
using Xenaia.Core;
using Xenaia.Data;
using Xenaia.Data.PostgreSql;
using Xenaia.Domain.Bookings;
using Xenaia.Modules.Sync;
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

// Booking-system seam (spec section 2), mirroring the helpdesk seam above.
// brighttide registers the adapter, the Sync module, and its endpoints; absent
// leaves Sync off (logged after build); any other value fails startup.
var bookingSystem = builder.Configuration["Providers:BookingSystem"];
var spreadsheet = builder.Configuration["Providers:Spreadsheet"];
var syncEnabled = false;
var spreadsheetEnabled = false;

switch (bookingSystem)
{
    case null or "":
        break;
    case "brighttide":
        builder.Services.AddSyncModule(
            builder.Configuration, spreadsheetConfigured: !string.IsNullOrEmpty(spreadsheet));
        builder.Services.AddBrightTideBookingSystem(builder.Configuration);
        syncEnabled = true;
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Providers:BookingSystem '{bookingSystem}'. Supported values: brighttide.");
}

// Spreadsheet seam: only meaningful when a booking system is on (nothing
// consumes ISpreadsheetGateway otherwise). googleworkspace without a booking
// system is a no-op (logged), not a startup failure; an unknown value fails.
switch (spreadsheet)
{
    case null or "":
        break;
    case "googleworkspace" when syncEnabled:
        builder.Services.AddGoogleWorkspaceSpreadsheets();
        spreadsheetEnabled = true;
        break;
    case "googleworkspace":
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Providers:Spreadsheet '{spreadsheet}'. Supported values: googleworkspace.");
}

builder.Services.AddHealthChecks().AddDbContextCheck<XenaiaDbContext>();

var app = builder.Build();

if (string.IsNullOrEmpty(helpdeskProvider))
    app.Logger.LogInformation("Triage is disabled: no Providers:Helpdesk configured");
if (!syncEnabled)
    app.Logger.LogInformation("Sync is disabled: no Providers:BookingSystem configured");
if (!spreadsheetEnabled)
    app.Logger.LogInformation("Spreadsheet features are disabled: no Providers:Spreadsheet configured");
if (string.IsNullOrEmpty(builder.Configuration["Api:ApiKey"]))
    app.Logger.LogWarning(
        "Api:ApiKey is not configured; all /api/* requests are refused with 503 until it is set");

app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health");
if (syncEnabled)
    app.MapSyncEndpoints();

app.Run();
