using Google.Apis.Sheets.v4.Data;

namespace Xenaia.Adapters.GoogleWorkspace.Tests;

/// <summary>
/// In-memory <see cref="ISheetsApi"/> test double. Values/Titles are the
/// canned response for the corresponding read; the *Exceptions queues let a
/// test script a sequence of failures (e.g. two 429s then success) that are
/// dequeued and thrown on each call before falling through to the canned
/// response or recording the call.
/// </summary>
internal sealed class FakeSheetsApi : ISheetsApi
{
    public IList<IList<object>>? Values { get; set; }
    public IReadOnlyList<string> Titles { get; set; } = [];

    public List<(string SpreadsheetId, IList<ValueRange> Data)> BatchUpdateCalls { get; } = [];
    public List<string> AddSheetTitles { get; } = [];

    public int GetValuesCallCount { get; private set; }
    public int BatchUpdateCallCount { get; private set; }
    public int GetSheetTitlesCallCount { get; private set; }
    public int AddSheetCallCount { get; private set; }

    public Queue<Exception> GetValuesExceptions { get; } = new();
    public Queue<Exception> BatchUpdateExceptions { get; } = new();
    public Queue<Exception> GetSheetTitlesExceptions { get; } = new();
    public Queue<Exception> AddSheetExceptions { get; } = new();

    public Task<IList<IList<object>>?> GetValuesAsync(string spreadsheetId, string range, CancellationToken ct)
    {
        GetValuesCallCount++;
        if (GetValuesExceptions.TryDequeue(out var ex))
            throw ex;
        return Task.FromResult(Values);
    }

    public Task BatchUpdateValuesAsync(string spreadsheetId, IList<ValueRange> data, CancellationToken ct)
    {
        BatchUpdateCallCount++;
        if (BatchUpdateExceptions.TryDequeue(out var ex))
            throw ex;
        BatchUpdateCalls.Add((spreadsheetId, data));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetSheetTitlesAsync(string spreadsheetId, CancellationToken ct)
    {
        GetSheetTitlesCallCount++;
        if (GetSheetTitlesExceptions.TryDequeue(out var ex))
            throw ex;
        return Task.FromResult(Titles);
    }

    public Task AddSheetAsync(string spreadsheetId, string title, CancellationToken ct)
    {
        AddSheetCallCount++;
        if (AddSheetExceptions.TryDequeue(out var ex))
            throw ex;
        AddSheetTitles.Add(title);
        return Task.CompletedTask;
    }
}
