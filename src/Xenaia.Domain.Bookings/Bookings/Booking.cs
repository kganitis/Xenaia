using Xenaia.Core.Domain;
using Xenaia.Domain.Bookings.Bookings.Events;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Bookings;

/// <summary>
/// Aggregate root for a reservation: owns its items, extras, payments and
/// gift cards, and enforces the sync state machine as behavior.
/// </summary>
public sealed class Booking : AggregateRoot<int>, ISyncTracked
{
    private readonly List<BookingItem> _items = [];
    private readonly List<BookingExtra> _extras = [];
    private readonly List<BookingPayment> _payments = [];
    private readonly List<BookingGiftCard> _giftCards = [];

    public BookingCode Code { get; private set; } = null!;

    public string SecretCode { get; private set; } = string.Empty;

    public string? Referrer { get; private set; }

    public string? ChannelBookingCode { get; private set; }

    public BookingType Type { get; private set; }

    public BookingStatus Status { get; private set; }

    public decimal FinalPrice { get; private set; }

    public string? ActivityLanguage { get; private set; }

    public string? LeadContactName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public DateTimeOffset? CreatedAtExternal { get; private set; }

    public DateTimeOffset? UpdatedAtExternal { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public SyncDirection Direction { get; private set; }

    public SyncState Sync { get; private set; } = SyncState.Pending;

    public IReadOnlyCollection<BookingItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<BookingExtra> Extras => _extras.AsReadOnly();

    public IReadOnlyCollection<BookingPayment> Payments => _payments.AsReadOnly();

    public IReadOnlyCollection<BookingGiftCard> GiftCards => _giftCards.AsReadOnly();

    private Booking(int id) : base(id) { }

    public static Booking Receive(
        BookingCode code,
        string secretCode,
        BookingType type,
        BookingStatus status,
        decimal finalPrice,
        SyncDirection direction,
        DateTimeOffset occurredAt)
    {
        if (status == BookingStatus.Cancelled)
            throw new BookingRuleViolationException(
                "A booking cannot be received already cancelled; receive it, then cancel.");
        if (finalPrice < 0)
            throw new BookingRuleViolationException("Final price cannot be negative.");

        var booking = new Booking(0)
        {
            Code = code,
            SecretCode = secretCode,
            Type = type,
            Status = status,
            FinalPrice = finalPrice,
            Direction = direction,
        };
        booking.Raise(new BookingReceived(code.Value, occurredAt));
        return booking;
    }

    public void UpdateContact(string? leadContactName, string? email, string? phone)
    {
        LeadContactName = leadContactName;
        Email = email;
        Phone = phone;
    }

    public void SetChannelReference(string? channelBookingCode, string? referrer)
    {
        ChannelBookingCode = channelBookingCode;
        Referrer = referrer;
    }

    public void SetActivityLanguage(string? language) => ActivityLanguage = language;

    public void SetExternalTimestamps(DateTimeOffset? createdAt, DateTimeOffset? updatedAt)
    {
        CreatedAtExternal = createdAt;
        UpdatedAtExternal = updatedAt;
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status == BookingStatus.Cancelled)
            throw new BookingRuleViolationException($"Booking {Code} is already cancelled.");

        Status = BookingStatus.Cancelled;
        CancelledAt = at;
        Raise(new BookingCancelled(Code.Value, at, at));
    }

    public BookingItem AddItem(
        int externalId,
        int externalProductId,
        int externalOptionId,
        string participantTypeAlias,
        DateTimeOffset activityAt,
        decimal finalPrice)
    {
        if (finalPrice < 0)
            throw new BookingRuleViolationException("Item price cannot be negative.");
        if (_items.Any(i => i.ExternalId == externalId))
            throw new BookingRuleViolationException(
                $"Booking {Code} already has an item with external id {externalId}.");

        var item = new BookingItem(
            externalId, externalProductId, externalOptionId,
            participantTypeAlias, activityAt, finalPrice);
        _items.Add(item);
        return item;
    }

    public BookingExtra AddExtra(
        int externalId,
        int externalOptionId,
        string extraAlias,
        string? title,
        DateTimeOffset? activityAt,
        int quantity,
        decimal finalPrice)
    {
        if (quantity <= 0)
            throw new BookingRuleViolationException("Extra quantity must be positive.");
        if (finalPrice < 0)
            throw new BookingRuleViolationException("Extra price cannot be negative.");
        if (_extras.Any(e => e.ExternalId == externalId))
            throw new BookingRuleViolationException(
                $"Booking {Code} already has an extra with external id {externalId}.");

        var extra = new BookingExtra(
            externalId, externalOptionId, extraAlias, title, activityAt, quantity, finalPrice);
        _extras.Add(extra);
        return extra;
    }

    public BookingPayment RecordPayment(
        int externalId,
        decimal amount,
        string? paymentMethod,
        PaymentStatus status,
        DateTimeOffset? paidAt)
    {
        if (amount < 0)
            throw new BookingRuleViolationException("Payment amount cannot be negative.");
        if (_payments.Any(p => p.ExternalId == externalId))
            throw new BookingRuleViolationException(
                $"Booking {Code} already has a payment with external id {externalId}.");

        var payment = new BookingPayment(externalId, amount, paymentMethod, status, paidAt);
        _payments.Add(payment);
        return payment;
    }

    public BookingGiftCard ApplyGiftCard(string code, decimal amount)
    {
        if (amount < 0)
            throw new BookingRuleViolationException("Gift card amount cannot be negative.");
        if (_giftCards.Any(g => g.Code == code))
            throw new BookingRuleViolationException(
                $"Booking {Code} already has gift card '{code}' applied.");

        var giftCard = new BookingGiftCard(code, amount);
        _giftCards.Add(giftCard);
        return giftCard;
    }

    public void CheckInItem(int externalId)
    {
        var item = _items.FirstOrDefault(i => i.ExternalId == externalId)
            ?? throw new BookingRuleViolationException(
                $"Booking {Code} has no item with external id {externalId}.");
        item.CheckIn();
    }

    public void ClaimForSync() => Sync = Sync.Claim();

    public void MarkSynced(DateTimeOffset at) => Sync = Sync.MarkSynced(at);

    public void MarkSyncFailed(string error, DateTimeOffset at)
    {
        Sync = Sync.MarkFailed(error);
        Raise(new BookingSyncFailed(Code.Value, error, at));
    }

    public void RequeueSync() => Sync = Sync.Requeue();
}
