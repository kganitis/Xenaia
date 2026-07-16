namespace Xenaia.Core.BusinessHours;

public interface IBusinessHoursService
{
    bool IsOpenNow();
    bool IsOpenAt(DateTimeOffset instant);

    /// <summary>Next opening strictly after the instant; null when no weekly windows are configured.</summary>
    DateTimeOffset? NextOpeningAfter(DateTimeOffset instant);
}
