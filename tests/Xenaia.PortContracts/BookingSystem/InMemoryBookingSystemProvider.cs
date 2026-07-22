using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.PortContracts.BookingSystem;

/// <summary>
/// Reference IBookingSystemProvider used by module tests, the Milestone 2
/// end-to-end test, and as the contract suite's baseline implementation.
/// Plain dictionaries behind a lock; not meant for concurrency stress, only
/// for deterministic, inspectable test fixtures.
/// </summary>
public sealed class InMemoryBookingSystemProvider : IBookingSystemProvider
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BookingSnapshot> _bookings = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ProductSnapshot> _products = [];
    private readonly Dictionary<int, List<ProductOptionSnapshot>> _productOptions = [];
    private readonly Dictionary<(int ProductExternalId, int OptionExternalId), List<AvailabilityTimeslot>> _availability = [];
    private readonly List<AvailabilityUpdate> _receivedAvailabilityUpdates = [];
    private readonly List<BookingDraft> _receivedCreates = [];
    private readonly List<string> _receivedCancels = [];
    private int _nextCode = 1000;

    public InMemoryBookingSystemProvider()
    {
        CodeGenerator = () => $"MT-{_nextCode++}";
    }

    /// <summary>Seeds a product and its options, bypassing the port (products
    /// are read-only through IBookingSystemProvider).</summary>
    public void SeedProduct(ProductSnapshot product, params ProductOptionSnapshot[] options)
    {
        lock (_lock)
        {
            _products[product.ExternalId] = product;
            _productOptions[product.ExternalId] = [.. options];
        }
    }

    /// <summary>Seeds a booking directly, bypassing CreateBookingAsync so
    /// tests can pin down fields (such as UpdatedAtExternal) the port itself
    /// would not let them control deterministically.</summary>
    public void SeedBooking(BookingSnapshot snapshot)
    {
        lock (_lock)
        {
            _bookings[snapshot.Code] = snapshot;
        }
    }

    /// <summary>Seeds availability timeslots for a product/option. Calling
    /// with no slots still registers the combination as known but empty,
    /// distinct from a product/option GetAvailabilityAsync has never heard
    /// of (which returns null).</summary>
    public void SeedAvailability(
        int productExternalId, int optionExternalId, params AvailabilityTimeslot[] slots)
    {
        lock (_lock)
        {
            _availability[(productExternalId, optionExternalId)] = [.. slots];
        }
    }

    public IReadOnlyList<AvailabilityUpdate> ReceivedAvailabilityUpdates
    {
        get { lock (_lock) { return [.. _receivedAvailabilityUpdates]; } }
    }

    public IReadOnlyList<BookingDraft> ReceivedCreates
    {
        get { lock (_lock) { return [.. _receivedCreates]; } }
    }

    public IReadOnlyList<string> ReceivedCancels
    {
        get { lock (_lock) { return [.. _receivedCancels]; } }
    }

    /// <summary>Generates the code assigned by CreateBookingAsync. Defaults
    /// to the sequence "MT-1000", "MT-1001", ...; tests may override it.</summary>
    public Func<string> CodeGenerator { get; set; }

    /// <summary>When set, the next call to any port method throws this
    /// exception and clears back to null (fires exactly once).</summary>
    public Exception? FailNextCallWith { get; set; }

    private void MaybeFail()
    {
        Exception? fault;
        lock (_lock)
        {
            fault = FailNextCallWith;
            FailNextCallWith = null;
        }
        if (fault is not null)
            throw fault;
    }

    public Task<IReadOnlyList<BookingSnapshot>> GetBookingsAsync(BookingQuery query, CancellationToken ct)
    {
        MaybeFail();
        lock (_lock)
        {
            IReadOnlyList<BookingSnapshot> result = _bookings.Values
                .Where(b => Matches(b, query))
                .OrderBy(b => b.UpdatedAtExternal ?? b.CreatedAtExternal ?? DateTimeOffset.MinValue)
                .ToList();
            return Task.FromResult(result);
        }
    }

    private static bool Matches(BookingSnapshot booking, BookingQuery query)
    {
        if (query.UpdatedFrom is { } updatedFrom &&
            (booking.UpdatedAtExternal is null || booking.UpdatedAtExternal < updatedFrom))
            return false;
        if (query.UpdatedTo is { } updatedTo &&
            (booking.UpdatedAtExternal is null || booking.UpdatedAtExternal > updatedTo))
            return false;
        if (query.BookedFrom is { } bookedFrom &&
            (booking.CreatedAtExternal is null || booking.CreatedAtExternal < bookedFrom))
            return false;
        if (query.BookedTo is { } bookedTo &&
            (booking.CreatedAtExternal is null || booking.CreatedAtExternal > bookedTo))
            return false;
        if (query.ActivityFrom is { } activityFrom &&
            !booking.Items.Any(item => item.ActivityAt >= activityFrom))
            return false;
        if (query.ActivityTo is { } activityTo &&
            !booking.Items.Any(item => item.ActivityAt <= activityTo))
            return false;
        if (query.Status is { } status && booking.Status != status)
            return false;
        if (query.Type is { } type && booking.Type != type)
            return false;
        if (query.Referrer is { } referrer &&
            !string.Equals(booking.Referrer, referrer, StringComparison.Ordinal))
            return false;
        return true;
    }

    public Task<BookingSnapshot?> GetBookingByCodeAsync(string code, CancellationToken ct)
    {
        MaybeFail();
        lock (_lock)
        {
            return Task.FromResult(_bookings.TryGetValue(code, out var booking) ? booking : null);
        }
    }

    public Task<BookingSnapshot> CreateBookingAsync(BookingDraft draft, CancellationToken ct)
    {
        lock (_lock) { _receivedCreates.Add(draft); }
        MaybeFail();
        draft.EnsureValid();

        string code;
        lock (_lock) { code = CodeGenerator(); }
        var now = DateTimeOffset.UtcNow;
        var snapshot = new BookingSnapshot
        {
            Code = code,
            SecretCode = $"SEC-{code}",
            Type = draft.Type,
            Status = BookingStatus.Pending,
            FinalPrice = draft.Items.Sum(item => item.FinalPrice),
            Referrer = draft.Referrer,
            LeadContactName = draft.LeadContactName,
            Email = draft.Email,
            Phone = draft.Phone,
            ActivityLanguage = draft.ActivityLanguage,
            CreatedAtExternal = now,
            UpdatedAtExternal = now,
            Items = draft.Items
                .Select((item, index) => new BookingItemSnapshot(
                    index + 1, item.ProductExternalId, item.OptionExternalId,
                    item.ParticipantTypeAlias, item.ActivityAt, item.FinalPrice))
                .ToList(),
        };

        lock (_lock) { _bookings[code] = snapshot; }
        return Task.FromResult(snapshot);
    }

    public Task CancelBookingAsync(string code, CancellationToken ct)
    {
        lock (_lock) { _receivedCancels.Add(code); }
        MaybeFail();

        lock (_lock)
        {
            if (!_bookings.TryGetValue(code, out var booking))
                throw new BookingSystemEntityNotFoundException($"Unknown booking code '{code}'.");

            _bookings[code] = booking with
            {
                Status = BookingStatus.Cancelled,
                CancelledAt = DateTimeOffset.UtcNow,
            };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailabilityTimeslot>?> GetAvailabilityAsync(
        int productExternalId, int optionExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        MaybeFail();
        lock (_lock)
        {
            if (!_availability.TryGetValue((productExternalId, optionExternalId), out var slots))
                return Task.FromResult<IReadOnlyList<AvailabilityTimeslot>?>(null);

            IReadOnlyList<AvailabilityTimeslot> filtered = slots
                .Where(slot => slot.At >= from && slot.At <= to)
                .ToList();
            return Task.FromResult<IReadOnlyList<AvailabilityTimeslot>?>(filtered);
        }
    }

    public Task UpdateAvailabilityAsync(AvailabilityUpdate update, CancellationToken ct)
    {
        lock (_lock) { _receivedAvailabilityUpdates.Add(update); }
        MaybeFail();

        lock (_lock)
        {
            var key = (update.ProductExternalId, update.OptionExternalId);
            if (!_availability.TryGetValue(key, out var slots))
            {
                slots = [];
                _availability[key] = slots;
            }

            if (update.Vacancies is not { } vacancies)
                return Task.CompletedTask;

            foreach (var at in TargetTimes(update))
            {
                var index = slots.FindIndex(slot => slot.At == at);
                var slot = new AvailabilityTimeslot(at, vacancies);
                if (index >= 0)
                    slots[index] = slot;
                else
                    slots.Add(slot);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Expands an update's From/To range (and optional daily
    /// Times) into the concrete instants it touches. Null Times means the
    /// update is slotless and applies at the single From instant.</summary>
    private static IEnumerable<DateTimeOffset> TargetTimes(AvailabilityUpdate update)
    {
        if (update.Times is not { Count: > 0 } times)
        {
            yield return update.From;
            yield break;
        }

        for (var day = update.From.Date; day <= update.To.Date; day = day.AddDays(1))
        {
            foreach (var time in times)
            {
                var at = new DateTimeOffset(day.Add(time.ToTimeSpan()), update.From.Offset);
                if (at >= update.From && at <= update.To)
                    yield return at;
            }
        }
    }

    public Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(CancellationToken ct)
    {
        MaybeFail();
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ProductSnapshot>>([.. _products.Values]);
        }
    }

    public Task<IReadOnlyList<ProductOptionSnapshot>> GetProductOptionsAsync(
        int productExternalId, CancellationToken ct)
    {
        MaybeFail();
        lock (_lock)
        {
            IReadOnlyList<ProductOptionSnapshot> options = _productOptions
                .TryGetValue(productExternalId, out var found) ? [.. found] : [];
            return Task.FromResult(options);
        }
    }
}
