using Xenaia.Domain.Bookings.Catalog;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Catalog;

public class CatalogEntityTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Category_defines_and_updates()
    {
        var category = Category.Define(3, "Water Activities", "Kayaks and paddleboards");

        category.Retitle("On-Water Activities");
        category.Redescribe(null);

        Assert.Equal(3, category.ExternalId);
        Assert.Equal("On-Water Activities", category.Title);
        Assert.Null(category.Description);
        Assert.Equal(SyncStatus.Pending, category.Sync.Status);
    }

    [Fact]
    public void Category_rejects_blank_title()
    {
        Assert.Throws<CatalogRuleViolationException>(() => Category.Define(3, " "));
        Assert.Throws<CatalogRuleViolationException>(() => Category.Define(4, ""));
        Assert.Throws<CatalogRuleViolationException>(() => Category.Define(5, "\t"));

        var category = Category.Define(6, "Valid Category");
        Assert.Throws<CatalogRuleViolationException>(() => category.Retitle("  "));
        Assert.Throws<CatalogRuleViolationException>(() => category.Retitle(""));
    }

    [Fact]
    public void Extra_rejects_blank_alias_and_negative_price()
    {
        Assert.Throws<CatalogRuleViolationException>(() => Extra.Define(" ", "Lunch"));
        Assert.Throws<CatalogRuleViolationException>(() => Extra.Define("lunch", "Lunch", price: -1m));

        var extra = Extra.Define("lunch", "Picnic lunch", "Sandwiches and fruit", 12.50m);
        extra.Reprice(14m);

        Assert.Equal(14m, extra.Price);
        Assert.Throws<CatalogRuleViolationException>(() => extra.Reprice(-2m));
    }

    [Fact]
    public void ParticipantType_defines()
    {
        var participant = ParticipantType.Define(7, "adult", "Adult");

        Assert.Equal(7, participant.ProductOptionId);
        Assert.Equal("adult", participant.Alias);
        Assert.Throws<CatalogRuleViolationException>(() => ParticipantType.Define(7, "", "Adult"));
    }

    [Fact]
    public void PaymentType_toggles_activity()
    {
        var paymentType = PaymentType.Define("card", "Credit card");

        paymentType.Deactivate();
        Assert.False(paymentType.IsActive);

        paymentType.Activate();
        Assert.True(paymentType.IsActive);
        Assert.Throws<CatalogRuleViolationException>(() => PaymentType.Define(" "));
    }

    [Fact]
    public void Catalog_entities_are_sync_tracked()
    {
        ISyncTracked[] entities =
        [
            Category.Define(3, "Water Activities"),
            Extra.Define("lunch", "Picnic lunch"),
            ParticipantType.Define(7, "adult", "Adult"),
            PaymentType.Define("card"),
        ];

        foreach (var entity in entities)
        {
            entity.ClaimForSync();
            entity.MarkSynced(At);
            Assert.Equal(SyncStatus.Synced, entity.Sync.Status);
        }
    }
}
