// The bare aggregate type name Xenaia.Domain.Bookings.Availabilities.Availability
// is shadowed project-wide by the Xenaia.Modules.Sync.Availability namespace
// (namespace member lookup walks the enclosing Sync namespace, where
// "Availability" names that nested namespace). One project-wide alias keeps
// every Sync file referring to the aggregate unambiguously.
global using AvailabilityAggregate = Xenaia.Domain.Bookings.Availabilities.Availability;
