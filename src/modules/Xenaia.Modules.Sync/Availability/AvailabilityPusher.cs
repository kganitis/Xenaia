using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xenaia.Core.Tenancy;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Domain.Bookings.Stores;
using Xenaia.Domain.Bookings.Sync;
using Xenaia.Modules.Sync.Catalog;
using Xenaia.Modules.Sync.Spreadsheets;

namespace Xenaia.Modules.Sync.Availability;

/// <summary>The result of pushing one availability work item, surfaced for
/// tests and for the processor service's logging.</summary>
public enum PushOutcome
{
    Synced,
    Failed,
    LostClaim,
}

/// <summary>
/// Claims one availability row, pushes it to the booking system with retry,
/// and buffers the sheet write-backs (spec 6.1 steps 3-4). Scoped: the
/// processor service resolves one per drain cycle and reuses it across the
/// items of that cycle, then flushes the shared <see cref="SheetWriteBuffer"/>.
/// The spreadsheet gateway is optional; when it is absent no sheet write-back
/// is buffered at all.
/// </summary>
public sealed class AvailabilityPusher
{
    private const int MaxErrorLength = 2000;
    private const string TimestampFormat = "yyyy-MM-dd HH:mm zzz";

    private readonly IAvailabilityStore _store;
    private readonly IBookingSystemProvider _provider;
    private readonly ParticipantTypeCache _participantTypeCache;
    private readonly AvailabilityChannel _channel;
    private readonly SheetWriteBuffer _buffer;
    private readonly SyncOptions _options;
    private readonly TenantProfileOptions _tenant;
    private readonly TimeProvider _clock;
    private readonly ISpreadsheetGateway? _gateway;
    private readonly ILogger<AvailabilityPusher> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer;

    public AvailabilityPusher(
        IAvailabilityStore store,
        IBookingSystemProvider provider,
        ParticipantTypeCache participantTypeCache,
        AvailabilityChannel channel,
        SheetWriteBuffer buffer,
        IOptions<SyncOptions> options,
        IOptions<TenantProfileOptions> tenantOptions,
        TimeProvider clock,
        ILogger<AvailabilityPusher> logger,
        ISpreadsheetGateway? gateway = null,
        Func<TimeSpan, CancellationToken, Task>? delayer = null)
    {
        _store = store;
        _provider = provider;
        _participantTypeCache = participantTypeCache;
        _channel = channel;
        _buffer = buffer;
        _options = options.Value;
        _tenant = tenantOptions.Value;
        _clock = clock;
        _logger = logger;
        _gateway = gateway;
        _delayer = delayer ?? ((delay, token) => Task.Delay(delay, clock, token));
    }

    /// <summary>Claims and pushes one work item; returns the outcome.</summary>
    public async Task<PushOutcome> ProcessAsync(AvailabilityWorkItem item, CancellationToken ct)
    {
        if (!await _store.TryClaimAsync(item.AvailabilityId, ct))
        {
            _logger.LogDebug(
                "Availability {Id}: already claimed or not Pending; nothing to push", item.AvailabilityId);
            return PushOutcome.LostClaim;
        }

        var row = await _store.GetByIdAsync(item.AvailabilityId, ct);
        if (row is null)
        {
            _logger.LogWarning("Availability {Id}: claimed but no longer loadable", item.AvailabilityId);
            return PushOutcome.LostClaim;
        }

        // Trap: the atomic claim is a set-based DB update that does not refresh
        // a tracked instance, so a row we already hold can still read Pending
        // after the claim. Reconcile the in-memory state to the DB's Processing
        // before marking the terminal state, so the state machine and the row
        // agree when SaveChangesAsync persists it.
        if (row.Sync.Status == SyncStatus.Pending)
            row.ClaimForSync();

        var aliases = await _participantTypeCache.GetAliasesAsync(row.ExternalProductId, row.ExternalOptionId, ct);
        var update = BuildUpdate(row, aliases);

        try
        {
            await Retry.ExecuteAsync(
                async token =>
                {
                    await _provider.UpdateAvailabilityAsync(update, token);
                    return true;
                },
                _options.Retry.Attempts,
                TimeSpan.FromSeconds(_options.Retry.BaseDelaySeconds),
                _delayer,
                ct);
        }
        catch (Exception ex)
        {
            // Capture the timestamp at the moment of failure (after retries),
            // not before the loop, so it reflects when the row actually failed.
            var failedAt = _clock.GetUtcNow();
            var error = Truncate(ex.Message);
            row.MarkSyncFailed(error, failedAt);
            await _store.SaveChangesAsync(ct);
            BufferFailure(item, error);
            _logger.LogWarning(
                ex, "Availability {Id}: push failed after retries; marked Failed", item.AvailabilityId);
            return PushOutcome.Failed;
        }

        var syncedAt = _clock.GetUtcNow();
        row.MarkSynced(syncedAt);
        await _store.SaveChangesAsync(ct);
        BufferSuccess(item, row, syncedAt);
        _logger.LogInformation("Availability {Id}: pushed and marked Synced", item.AvailabilityId);
        return PushOutcome.Synced;
    }

    /// <summary>Startup recovery: reset stuck Processing rows to Pending, then
    /// re-enqueue every Pending row (with no sheet context). Returns the number
    /// of rows re-enqueued.</summary>
    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        var reset = await _store.ResetProcessingAsync(ct);

        // A single wide fetch: enqueuing leaves rows Pending, so a batched
        // re-read would see the same rows again. At-least-once delivery makes a
        // duplicate enqueue harmless (the claim de-duplicates).
        var pending = await _store.GetPendingAsync(int.MaxValue, ct);
        foreach (var row in pending)
            await _channel.Writer.WriteAsync(new AvailabilityWorkItem(row.Id, Sheet: null), ct);

        _logger.LogInformation(
            "Availability recovery: {Reset} stuck rows reset, {Enqueued} pending rows re-enqueued",
            reset, pending.Count);
        return pending.Count;
    }

    /// <summary>
    /// A timeslot of exactly 00:00 is the slotless sentinel: the update omits
    /// times so the vendor applies it across the whole day range. Any other
    /// time sends that single time-of-day. Either way the range is the day's
    /// 00:01 to 23:59 (midnight is deliberately avoided).
    /// </summary>
    private static AvailabilityUpdate BuildUpdate(AvailabilityAggregate row, IReadOnlyList<string> aliases)
    {
        var offset = row.TimeslotAt.Offset;
        var date = row.TimeslotAt.Date;
        var from = new DateTimeOffset(date.AddMinutes(1), offset);            // 00:01
        var to = new DateTimeOffset(date.Add(new TimeSpan(23, 59, 0)), offset); // 23:59

        var timeOfDay = TimeOnly.FromTimeSpan(row.TimeslotAt.TimeOfDay);
        IReadOnlyList<TimeOnly>? times = timeOfDay == TimeOnly.MinValue ? null : [timeOfDay];

        return new AvailabilityUpdate(
            row.ExternalProductId, row.ExternalOptionId,
            from, to, times, row.Vacancies, row.StopSales, aliases);
    }

    private void BufferSuccess(AvailabilityWorkItem item, AvailabilityAggregate row, DateTimeOffset now)
    {
        if (_gateway is null || item.Sheet is not { } sheet)
            return;

        var timestamp = FormatTimestamp(now);
        if (sheet.PatchStatusRange is { } patchRange)
            _buffer.AddPatchStatus(sheet.SpreadsheetId, patchRange, $"Synced at {timestamp}");

        _buffer.AddGetRow(
            sheet.SpreadsheetId,
            new SheetWriteBuffer.GetRowKey(row.ExternalProductId, row.ExternalOptionId, row.TimeslotAt),
            row.Vacancies, timestamp, row.StopSales);
    }

    private void BufferFailure(AvailabilityWorkItem item, string error)
    {
        if (_gateway is null || item.Sheet is not { } sheet)
            return;
        if (sheet.PatchStatusRange is { } patchRange)
            _buffer.AddPatchStatus(sheet.SpreadsheetId, patchRange, error);
    }

    private string FormatTimestamp(DateTimeOffset utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_tenant.TimeZone);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return local.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    private static string Truncate(string value)
        => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
