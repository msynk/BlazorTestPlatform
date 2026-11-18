using Microsoft.Extensions.Time.Testing;

namespace BlazorTestPlatform.Core.Tests;

public sealed class StoryClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 23, 59, 0, TimeSpan.Zero);

    private static Task Advance(StoryClock clock, TimeSpan delta) =>
        clock.AdvanceAsync(delta, step => { clock.Fake.Advance(step); return Task.CompletedTask; }, () => Task.Delay(1));

    [Fact]
    public async Task Setting_the_system_time_moves_the_wall_clock_only()
    {
        var clock = new StoryClock(new FakeTimeProvider(Start));
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        var stamp = clock.GetTimestamp();

        clock.SetSystemTime(Start.AddHours(9));

        Assert.Equal(Start.AddHours(9), clock.GetUtcNow());
        Assert.Equal(0, fired);
        Assert.Equal(TimeSpan.Zero, clock.GetElapsedTime(stamp));

        // Running time still fires the timer on its own schedule, and the wall clock keeps its jump.
        await Advance(clock, TimeSpan.FromMinutes(1));
        Assert.Equal(1, fired);
        Assert.Equal(Start.AddHours(9).AddMinutes(1), clock.GetUtcNow());

        clock.SetSystemTime(Start);
        Assert.Equal(Start, clock.GetUtcNow());
    }

    [Fact]
    public async Task Advancing_runs_every_occurrence_of_a_delay_loop()
    {
        var clock = new StoryClock(new FakeTimeProvider(Start));
        var ticks = 0;
        using var stop = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), clock, stop.Token);
                    Interlocked.Increment(ref ticks);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
        await WaitUntil(() => TimersOf(clock) > 0);

        // A settle that waits for the loop to schedule its next delay (as drivers wait for the renderer).
        var expected = 0;
        await clock.AdvanceAsync(
            TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(30)),
            step => { clock.Fake.Advance(step); return Task.CompletedTask; },
            async () => { expected = Math.Min(expected + 1, 3); await WaitUntil(() => Volatile.Read(ref ticks) >= expected && TimersOf(clock) > 0); });

        Assert.Equal(3, Volatile.Read(ref ticks));
        Assert.Equal(Start.AddMinutes(3).AddSeconds(30), clock.GetUtcNow());
        await stop.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Periodic_timers_fire_once_per_period_and_disposed_ones_not_at_all()
    {
        var clock = new StoryClock(new FakeTimeProvider(Start));
        var periodic = 0;
        var disposed = 0;
        using var every = clock.CreateTimer(_ => periodic++, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        clock.CreateTimer(_ => disposed++, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan).Dispose();

        await Advance(clock, TimeSpan.FromSeconds(45));

        Assert.Equal(4, periodic);
        Assert.Equal(0, disposed);
    }

    [Fact]
    public async Task A_timer_due_now_is_fired_before_time_moves_on()
    {
        var clock = new StoryClock(new FakeTimeProvider(Start));
        var firedAt = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(_ => firedAt.Add(clock.GetUtcNow()), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        await Advance(clock, TimeSpan.FromMinutes(2));

        // Occurrences at 0, 1 and 2 minutes, each seen at its own instant.
        Assert.Equal([Start, Start.AddMinutes(1), Start.AddMinutes(2)], firedAt.Distinct().ToArray());
    }

    [Fact]
    public async Task Too_many_occurrences_in_one_advance_are_reported()
    {
        var clock = new StoryClock(new FakeTimeProvider(Start));
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            clock.AdvanceAsync(TimeSpan.FromMinutes(5), step => { clock.Fake.Advance(step); return Task.CompletedTask; }, () => Task.CompletedTask));

        Assert.Contains("more than 100,000 timer occurrences", error.Message, StringComparison.Ordinal);
    }

    private static int TimersOf(StoryClock clock) => clock.TimerCount;

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(2);
        }
    }
}
