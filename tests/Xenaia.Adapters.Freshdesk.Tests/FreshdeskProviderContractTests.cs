using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.PortContracts.Helpdesk;

namespace Xenaia.Adapters.Freshdesk.Tests;

/// <summary>
/// The reusable port contract, run against the real adapter over a stateful
/// fake Freshdesk server. What the in-memory provider promises, Freshdesk
/// must promise too.
/// </summary>
public class FreshdeskProviderContractTests : HelpdeskProviderContract
{
    private static readonly FreshdeskOptions Options = new()
    {
        BaseUrl = "https://meridian.example/api/v2/",
        ApiKey = "test-key",
        PageSize = 2,
        FieldMap = new Dictionary<string, string>
        {
            ["bookingCode"] = "cf_booking_code",
            ["channel"] = "cf_channel",
        },
    };

    private FreshdeskFakeServerHandler? _server;

    protected override Task<IHelpdeskProvider> CreateProviderAsync(
        IReadOnlyList<HelpdeskTicket> seed)
    {
        _server = new FreshdeskFakeServerHandler(seed, Options.FieldMap);
        var http = new HttpClient(_server) { BaseAddress = new Uri(Options.BaseUrl) };
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes("test-key:X"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        return Task.FromResult<IHelpdeskProvider>(new FreshdeskHelpdeskProvider(
            http, Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<FreshdeskHelpdeskProvider>.Instance));
    }

    protected override Task<HelpdeskTicket> GetTicketSnapshotAsync(string id) =>
        Task.FromResult(_server!.Snapshot(id));

    protected override Task<IReadOnlyList<string>> GetNotesAsync(string id) =>
        Task.FromResult(_server!.Notes(id));
}
