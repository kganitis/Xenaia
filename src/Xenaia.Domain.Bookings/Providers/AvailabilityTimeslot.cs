namespace Xenaia.Domain.Bookings.Providers;

public sealed record AvailabilityTimeslot(DateTimeOffset At, int Vacancies);
