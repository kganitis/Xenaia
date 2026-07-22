using Xenaia.Domain.Bookings.Bookings;
using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Domain.Bookings.Tests.Providers;

public class BookingDraftTests
{
    private static readonly DateTimeOffset ActivityAt = new(2026, 7, 22, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void EnsureValid_passes_for_valid_draft()
    {
        var draft = new BookingDraft
        {
            Type = BookingType.Api,
            Items = new[]
            {
                new BookingDraftItem(101, 1, "adult", ActivityAt, 150m)
            }
        };

        draft.EnsureValid();
    }

    [Fact]
    public void EnsureValid_throws_when_items_empty()
    {
        var draft = new BookingDraft
        {
            Items = Array.Empty<BookingDraftItem>()
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_items_null()
    {
        var draft = new BookingDraft
        {
            Items = null!
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_product_id_non_positive()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(0, 1, "adult", ActivityAt, 150m)
            }
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_option_id_non_positive()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 0, "adult", ActivityAt, 150m)
            }
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_participant_type_alias_blank()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 1, "", ActivityAt, 150m)
            }
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_participant_type_alias_whitespace()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 1, "  ", ActivityAt, 150m)
            }
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_throws_when_price_negative()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 1, "adult", ActivityAt, -10m)
            }
        };

        Assert.Throws<ArgumentException>(() => draft.EnsureValid());
    }

    [Fact]
    public void EnsureValid_passes_with_multiple_items()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 1, "adult", ActivityAt, 150m),
                new BookingDraftItem(101, 1, "child", ActivityAt.AddDays(1), 75m)
            }
        };

        draft.EnsureValid();
    }

    [Fact]
    public void EnsureValid_passes_with_zero_price()
    {
        var draft = new BookingDraft
        {
            Items = new[]
            {
                new BookingDraftItem(101, 1, "adult", ActivityAt, 0m)
            }
        };

        draft.EnsureValid();
    }
}
