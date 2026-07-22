using Xenaia.Domain.Bookings.Providers;

namespace Xenaia.Modules.Sync;

/// <summary>
/// Shared exponential-backoff retry for vendor writes. All five Sync flows
/// treat a <see cref="BookingSystemException"/> as retryable and a
/// <see cref="BookingSystemEntityNotFoundException"/> as permanent.
/// </summary>
public static class Retry
{
    /// <summary>
    /// Runs <paramref name="action"/> up to <paramref name="attempts"/> times,
    /// delaying base*2^n seconds between tries (2s, 4s, 8s for base 2, attempts
    /// 4). Rethrows the last failure once the attempts are exhausted. Never
    /// retries <see cref="BookingSystemEntityNotFoundException"/>.
    /// </summary>
    public static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action, int attempts,
        TimeSpan baseDelay, TimeProvider clock, CancellationToken ct)
        => ExecuteAsync(action, attempts, baseDelay,
            (delay, token) => Task.Delay(delay, clock, token), ct);

    /// <summary>Delayer-injecting overload: tests fake the delayer so they
    /// never sleep and can assert the exact backoff sequence.</summary>
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action, int attempts,
        TimeSpan baseDelay, Func<TimeSpan, CancellationToken, Task> delayer,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (BookingSystemEntityNotFoundException)
            {
                // Permanent: the entity does not exist, retrying cannot help.
                throw;
            }
            catch (Exception) when (attempt < attempts - 1)
            {
                var delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * Math.Pow(2, attempt));
                await delayer(delay, ct);
            }
        }
    }
}
