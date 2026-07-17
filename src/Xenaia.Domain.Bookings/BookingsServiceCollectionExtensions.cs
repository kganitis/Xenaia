using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Core.Options;
using Xenaia.Domain.Bookings.Codes;

namespace Xenaia.Domain.Bookings;

public static class BookingsServiceCollectionExtensions
{
    /// <summary>
    /// Bookings-domain registrations: tenant code formats, validated
    /// fail-closed at startup.
    /// </summary>
    public static IServiceCollection AddBookingsDomain(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<BookingsFormatOptions>(configuration);
        services.AddSingleton<IValidateOptions<BookingsFormatOptions>, BookingsFormatValidator>();
        services.AddSingleton<CodeFormats>();
        return services;
    }
}
