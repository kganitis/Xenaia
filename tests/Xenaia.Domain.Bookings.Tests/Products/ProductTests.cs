using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Products;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings.Tests.Products;

public class ProductTests
{
    private static readonly CodeFormat Format = CodeFormat.Create("^MTP-[A-Z0-9]{4}$");
    private static readonly DateTimeOffset At = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Define_starts_pending_with_no_code()
    {
        var product = Product.Define(42, "Sunset Kayak Tour", categoryId: 3);

        Assert.Equal(42, product.ExternalId);
        Assert.Equal("Sunset Kayak Tour", product.Title);
        Assert.Equal(3, product.CategoryId);
        Assert.Null(product.Code);
        Assert.Equal(SyncStatus.Pending, product.Sync.Status);
    }

    [Fact]
    public void Define_rejects_blank_title()
    {
        Assert.Throws<ProductRuleViolationException>(() => Product.Define(42, "  "));
    }

    [Fact]
    public void AssignCode_and_Retitle_update_the_product()
    {
        var product = Product.Define(42, "Sunset Kayak Tour");

        product.AssignCode(ProductCode.Create("MTP-K4Y2", Format));
        product.Retitle("Sunset Kayak Adventure");
        product.Recategorize(9);

        Assert.Equal("MTP-K4Y2", product.Code!.Value);
        Assert.Equal("Sunset Kayak Adventure", product.Title);
        Assert.Equal(9, product.CategoryId);
    }

    [Fact]
    public void AddOption_guards_duplicates_and_LinkExtra_guards_duplicates()
    {
        var product = Product.Define(42, "Sunset Kayak Tour");

        var option = product.AddOption(7, "Two-seat kayak");
        option.LinkExtra(31);

        Assert.Single(product.Options);
        Assert.Single(option.ExtraLinks);
        Assert.Throws<ProductRuleViolationException>(() => product.AddOption(7, "Again"));
        Assert.Throws<ProductRuleViolationException>(() => option.LinkExtra(31));
    }

    [Fact]
    public void Sync_transitions_work_on_products()
    {
        var product = Product.Define(42, "Sunset Kayak Tour");

        product.ClaimForSync();
        product.MarkSynced(At);

        Assert.Equal(SyncStatus.Synced, product.Sync.Status);
        Assert.Throws<InvalidSyncTransitionException>(() => product.MarkSyncFailed("late", At));
    }
}
