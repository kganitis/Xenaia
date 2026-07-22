using System.Net;
using Google.Apis.Sheets.v4.Data;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Adapters.GoogleWorkspace;

/// <summary>
/// <see cref="ISpreadsheetGateway"/> over <see cref="ISheetsApi"/>: all the
/// logic that is worth unit testing without Google lives here. Maps
/// object cell values to strings (null cells become empty string), chunks
/// batch writes to the API's per-request range limit, retries a 429
/// ("rate limited") response up to twice with a fixed delay aligned to the
/// per-minute write quota (a third failure propagates), and makes
/// <see cref="EnsureTabAsync"/> idempotent by swallowing the already-exists
/// race. Scoped: a fresh instance per unit of work, matching the
/// Freshdesk/BrightTide adapter precedent for provider registrations.
/// </summary>
internal sealed class GoogleSpreadsheetGateway : ISpreadsheetGateway
{
    private const int MaxRangesPerBatch = 100;
    private const int MaxAttempts = 3; // one initial try plus up to two retries
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);
    private const HttpStatusCode TooManyRequests = (HttpStatusCode)429;

    private readonly ISheetsApi _api;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer;

    public GoogleSpreadsheetGateway(ISheetsApi api, Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        _api = api;
        _delayer = delayer ?? ((delay, ct) => Task.Delay(delay, ct));
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> GetValuesAsync(
        string spreadsheetId, string range, CancellationToken ct)
    {
        var raw = await ExecuteWithRetryAsync(token => _api.GetValuesAsync(spreadsheetId, range, token), ct);
        if (raw is null)
            return [];

        return raw
            .Select(row => (IReadOnlyList<string>)(row ?? []).Select(ToCellString).ToList())
            .ToList();
    }

    public async Task BatchUpdateAsync(
        string spreadsheetId, IReadOnlyList<SheetValueRange> updates, CancellationToken ct)
    {
        var valueRanges = updates
            .Select(update => new ValueRange
            {
                Range = update.Range,
                Values = update.Rows
                    .Select(row => (IList<object>)row.Select(cell => (object)cell).ToList())
                    .ToList(),
            })
            .ToList();

        foreach (var chunk in Chunk(valueRanges, MaxRangesPerBatch))
        {
            await ExecuteWithRetryAsync(
                async token =>
                {
                    await _api.BatchUpdateValuesAsync(spreadsheetId, chunk, token);
                    return true;
                },
                ct);
        }
    }

    public async Task EnsureTabAsync(string spreadsheetId, string title, CancellationToken ct)
    {
        var titles = await ExecuteWithRetryAsync(
            token => _api.GetSheetTitlesAsync(spreadsheetId, token), ct);
        if (titles.Contains(title))
            return;

        try
        {
            await ExecuteWithRetryAsync(
                async token =>
                {
                    await _api.AddSheetAsync(spreadsheetId, title, token);
                    return true;
                },
                ct);
        }
        catch (Google.GoogleApiException ex) when (IsAlreadyExists(ex))
        {
            // Another writer created the tab between our list and our add;
            // the tab existing is exactly the outcome we wanted.
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Google.GoogleApiException ex) when (IsTooManyRequests(ex) && attempt < MaxAttempts)
            {
                await _delayer(RetryDelay, ct);
            }
        }
    }

    private static bool IsTooManyRequests(Google.GoogleApiException ex) => ex.HttpStatusCode == TooManyRequests;

    private static bool IsAlreadyExists(Google.GoogleApiException ex) =>
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IList<ValueRange>> Chunk(IReadOnlyList<ValueRange> source, int size)
    {
        for (var offset = 0; offset < source.Count; offset += size)
            yield return source.Skip(offset).Take(size).ToList();
    }

    private static string ToCellString(object? cell) => cell?.ToString() ?? "";
}
