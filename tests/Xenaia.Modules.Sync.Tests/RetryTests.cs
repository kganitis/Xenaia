using Microsoft.Extensions.Time.Testing;
using Xenaia.Domain.Bookings.Providers;
using Xenaia.Modules.Sync;

namespace Xenaia.Modules.Sync.Tests;

public class RetryTests
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Succeeds_on_the_first_try_calls_action_once_and_never_delays()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();

        var result = await Retry.ExecuteAsync(
            _ => { calls++; return Task.FromResult(42); },
            attempts: 4, BaseDelay, Delayer(delays), CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Fails_twice_then_succeeds_calls_three_times_with_2s_then_4s_delays()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();

        var result = await Retry.ExecuteAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                    throw new BookingSystemException("transient");
                return Task.FromResult(7);
            },
            attempts: 4, BaseDelay, Delayer(delays), CancellationToken.None);

        Assert.Equal(7, result);
        Assert.Equal(3, calls);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], delays);
    }

    [Fact]
    public async Task Exhausts_after_attempts_and_rethrows_the_last_failure()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();

        var ex = await Assert.ThrowsAsync<BookingSystemException>(() => Retry.ExecuteAsync<int>(
            _ => { calls++; throw new BookingSystemException($"boom {calls}"); },
            attempts: 4, BaseDelay, Delayer(delays), CancellationToken.None));

        Assert.Equal("boom 4", ex.Message);
        Assert.Equal(4, calls); // 4 attempts total
        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], delays); // 3 backoffs
    }

    [Fact]
    public async Task Never_retries_a_not_found_failure()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();

        await Assert.ThrowsAsync<BookingSystemEntityNotFoundException>(() => Retry.ExecuteAsync<int>(
            _ => { calls++; throw new BookingSystemEntityNotFoundException("gone"); },
            attempts: 4, BaseDelay, Delayer(delays), CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Public_time_provider_overload_returns_the_action_result_without_sleeping()
    {
        // No failure, so no delay elapses: the public TimeProvider overload
        // (the verbatim contract for the later tasks) completes immediately.
        var result = await Retry.ExecuteAsync(
            _ => Task.FromResult("ok"),
            attempts: 4, BaseDelay, new FakeTimeProvider(), CancellationToken.None);

        Assert.Equal("ok", result);
    }

    private static Func<TimeSpan, CancellationToken, Task> Delayer(List<TimeSpan> sink)
        => (delay, _) => { sink.Add(delay); return Task.CompletedTask; };
}
