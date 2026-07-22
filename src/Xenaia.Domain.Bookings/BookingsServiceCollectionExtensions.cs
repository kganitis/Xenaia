using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Domain.Bookings.Codes;
using Xenaia.Domain.Bookings.Sync;

namespace Xenaia.Domain.Bookings;

public static class BookingsServiceCollectionExtensions
{
    /// <summary>
    /// Bookings-domain registrations: tenant code formats, validated
    /// fail-closed at startup, and the booking ingest service.
    /// </summary>
    public static IServiceCollection AddBookingsDomain(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<BookingsFormatOptions>(configuration);
        services.AddSingleton<IValidateOptions<BookingsFormatOptions>, BookingsFormatValidator>();
        services.AddSingleton<CodeFormats>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<BookingIngestService>();
        return services;
    }
}
