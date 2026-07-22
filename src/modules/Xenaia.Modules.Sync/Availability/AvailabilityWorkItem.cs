namespace Xenaia.Modules.Sync.Availability;

/// <summary>One durable-queue row ready for the availability processor to
/// claim, plus optional sheet write-back context (absent for recovered items).</summary>
public sealed record AvailabilityWorkItem(int AvailabilityId, SheetWriteContext? Sheet);
