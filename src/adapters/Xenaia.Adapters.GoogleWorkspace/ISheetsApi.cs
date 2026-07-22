using Google.Apis.Sheets.v4.Data;

namespace Xenaia.Adapters.GoogleWorkspace;

/// <summary>
/// The thin seam over <c>SheetsService</c> that <see cref="GoogleSpreadsheetGateway"/>
/// depends on: exactly the surface the gateway needs, nothing more. Faking this
/// interface in tests keeps the gateway's mapping, chunking, and retry logic
/// unit-testable without Google.
/// </summary>
internal interface ISheetsApi
{
    Task<IList<IList<object>>?> GetValuesAsync(string spreadsheetId, string range, CancellationToken ct);

    /// <summary>Writes with <c>USER_ENTERED</c> value input option.</summary>
    Task BatchUpdateValuesAsync(string spreadsheetId, IList<ValueRange> data, CancellationToken ct);

    Task<IReadOnlyList<string>> GetSheetTitlesAsync(string spreadsheetId, CancellationToken ct);

    Task AddSheetAsync(string spreadsheetId, string title, CancellationToken ct);
}
