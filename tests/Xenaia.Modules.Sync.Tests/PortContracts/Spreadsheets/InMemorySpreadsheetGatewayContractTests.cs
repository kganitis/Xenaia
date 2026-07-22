using Xenaia.Modules.Sync.Spreadsheets;
using Xenaia.PortContracts.Spreadsheets;

namespace Xenaia.Modules.Sync.Tests.PortContracts.Spreadsheets;

public class InMemorySpreadsheetGatewayContractTests : SpreadsheetGatewayContractTests
{
    protected override Task<ISpreadsheetGateway> CreateGatewayAsync() =>
        Task.FromResult<ISpreadsheetGateway>(new InMemorySpreadsheetGateway());
}
