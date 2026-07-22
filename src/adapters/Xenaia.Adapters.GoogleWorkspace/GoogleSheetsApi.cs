using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Xenaia.Adapters.GoogleWorkspace;

/// <summary>
/// Real <see cref="ISheetsApi"/> over the Google Sheets v4 SDK. A thin
/// passthrough with no branching logic of its own; not unit tested for that
/// reason. Everything testable (mapping, chunking, retry, idempotent tab
/// creation) lives in <see cref="GoogleSpreadsheetGateway"/>. Registered as a
/// singleton over a singleton <see cref="SheetsService"/>.
/// </summary>
internal sealed class GoogleSheetsApi(SheetsService sheets) : ISheetsApi
{
    public async Task<IList<IList<object>>?> GetValuesAsync(string spreadsheetId, string range, CancellationToken ct)
    {
        var response = await sheets.Spreadsheets.Values.Get(spreadsheetId, range).ExecuteAsync(ct);
        return response.Values;
    }

    public async Task BatchUpdateValuesAsync(string spreadsheetId, IList<ValueRange> data, CancellationToken ct)
    {
        var request = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED",
            Data = data,
        };
        await sheets.Spreadsheets.Values.BatchUpdate(request, spreadsheetId).ExecuteAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetSheetTitlesAsync(string spreadsheetId, CancellationToken ct)
    {
        var spreadsheet = await sheets.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
        return (spreadsheet.Sheets ?? [])
            .Select(sheet => sheet.Properties?.Title)
            .Where(title => title is not null)
            .Select(title => title!)
            .ToList();
    }

    public async Task AddSheetAsync(string spreadsheetId, string title, CancellationToken ct)
    {
        var request = new BatchUpdateSpreadsheetRequest
        {
            Requests =
            [
                new Request
                {
                    AddSheet = new AddSheetRequest
                    {
                        Properties = new SheetProperties { Title = title },
                    },
                },
            ],
        };
        await sheets.Spreadsheets.BatchUpdate(request, spreadsheetId).ExecuteAsync(ct);
    }
}
