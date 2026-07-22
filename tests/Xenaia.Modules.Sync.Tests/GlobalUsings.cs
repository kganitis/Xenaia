// A sibling test namespace Xenaia.Modules.Sync.Tests.Availability shadows the
// bare aggregate type name Xenaia.Domain.Bookings.Availabilities.Availability
// project-wide (namespace member lookup walks the enclosing
// Xenaia.Modules.Sync.Tests namespace, where "Availability" now names that
// nested namespace). One project-wide alias keeps every test file referring to
// the aggregate unambiguously.
global using AvailabilityAggregate = Xenaia.Domain.Bookings.Availabilities.Availability;
