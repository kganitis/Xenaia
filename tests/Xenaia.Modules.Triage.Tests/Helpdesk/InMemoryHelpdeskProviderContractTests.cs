using Xenaia.Modules.Triage.Helpdesk;
using Xenaia.PortContracts.Helpdesk;

namespace Xenaia.Modules.Triage.Tests.Helpdesk;

public class InMemoryHelpdeskProviderContractTests : HelpdeskProviderContract
{
    private InMemoryHelpdeskProvider? _provider;

    protected override Task<IHelpdeskProvider> CreateProviderAsync(
        IReadOnlyList<HelpdeskTicket> seed)
    {
        _provider = new InMemoryHelpdeskProvider(seed);
        return Task.FromResult<IHelpdeskProvider>(_provider);
    }

    protected override Task<HelpdeskTicket> GetTicketSnapshotAsync(string id) =>
        Task.FromResult(_provider!.Ticket(id));

    protected override Task<IReadOnlyList<string>> GetNotesAsync(string id) =>
        Task.FromResult(_provider!.Notes(id));
}
