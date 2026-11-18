using Microsoft.Extensions.Time.Testing;

namespace BlazorTestPlatform;

/// <summary>
/// The <see cref="TimeProvider"/> of a story with fake time (<c>UseFakeTime</c>): a <see cref="FakeTimeProvider"/> whose
/// wall clock can also jump without moving anything else, as a user changing the system clock does and as Playwright's
/// <c>clock.setSystemTime</c>. Timers, <see cref="Task.Delay(TimeSpan, TimeProvider)"/> and timestamps (stopwatches) follow
/// <see cref="Fake"/>; <see cref="GetUtcNow"/> adds the jumps. The clock knows when its timers are due, so
/// <see cref="AdvanceAsync"/> fires every occurrence on the way (a one-minute loop advanced by three minutes runs three
/// times), as a real clock and Playwright's <c>clock.runFor</c> do.
/// </summary>
public sealed class StoryClock : TimeProvider
{
    // A cap on the timer occurrences one advance fires, so a timer with a tiny period cannot spin for ever.
    private const int MaxSteps = 100_000;

    private readonly Lock _gate = new();
    private readonly HashSet<TrackedTimer> _timers = [];
    private long _offsetTicks;

    /// <summary>Wraps <paramref name="fake"/>.</summary>
    public StoryClock(FakeTimeProvider fake) => Fake = fake ?? throw new ArgumentNullException(nameof(fake));

    /// <summary>The fake time that timers and timestamps follow.</summary>
    public FakeTimeProvider Fake { get; }

    /// <summary>Timers created through the clock and not disposed (tests).</summary>
    internal int TimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>How far the wall clock was moved away from <see cref="Fake"/> by <see cref="SetSystemTime"/>.</summary>
    public TimeSpan WallClockOffset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Fake.GetUtcNow() + WallClockOffset;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => Fake.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => Fake.TimestampFrequency;

    /// <inheritdoc />
    public override long GetTimestamp() => Fake.GetTimestamp();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new TrackedTimer(this, Fake.GetUtcNow(), dueTime, period);
        timer.Attach(Fake.CreateTimer(s => { timer.Fired(); callback(s); }, state, dueTime, period));
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>
    /// Makes the wall clock read <paramref name="now"/> (forwards or backwards) without firing timers or moving timestamps:
    /// only what reads <see cref="GetUtcNow"/> (and local time) sees the change.
    /// </summary>
    public void SetSystemTime(DateTimeOffset now) => Interlocked.Exchange(ref _offsetTicks, (now - Fake.GetUtcNow()).Ticks);

    /// <summary>
    /// Runs fake time forward by <paramref name="delta"/>, stopping at every timer occurrence due on the way: each stop
    /// <paramref name="advance"/>s to it (drivers run it on the renderer) and then lets the story <paramref name="settle"/>,
    /// so work a timer starts (the next <c>Task.Delay</c> of a loop) is scheduled before time moves on.
    /// </summary>
    public async Task AdvanceAsync(TimeSpan delta, Func<TimeSpan, Task> advance, Func<Task> settle)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(advance);
        ArgumentNullException.ThrowIfNull(settle);
        var target = Fake.GetUtcNow() + delta;
        var steps = 0;
        while (NextDue(target) is { } due)
        {
            if (++steps > MaxSteps)
            {
                throw new InvalidOperationException(
                    $"Advancing the story clock by {delta} would fire more than {MaxSteps:N0} timer occurrences (the next is due at {due:O}): a timer with a tiny period? Advance in smaller steps.");
            }

            // A due instant can equal now (a timer armed with a zero due time): a zero step fires it.
            var step = due - Fake.GetUtcNow();
            await advance(step > TimeSpan.Zero ? step : TimeSpan.Zero).ConfigureAwait(false);
            await settle().ConfigureAwait(false);
        }

        var rest = target - Fake.GetUtcNow();
        if (rest > TimeSpan.Zero)
        {
            await advance(rest).ConfigureAwait(false);
        }

        await settle().ConfigureAwait(false);
    }

    // The earliest occurrence not fired yet that is due by the target, or null.
    private DateTimeOffset? NextDue(DateTimeOffset target)
    {
        lock (_gate)
        {
            DateTimeOffset? next = null;
            foreach (var timer in _timers)
            {
                if (timer.NextDue() is { } due && due <= target && (next is null || due < next))
                {
                    next = due;
                }
            }

            return next;
        }
    }

    private void Forget(TrackedTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    // Records when a timer is due and which occurrences fired (its callback is wrapped), so advances stop at each
    // occurrence not fired yet; firing stays with FakeTimeProvider, which keeps periodic timers on their grid.
    private sealed class TrackedTimer : ITimer
    {
        private readonly StoryClock _clock;
        private readonly Lock _gate = new();
        private ITimer _inner = null!;
        private DateTimeOffset? _due;
        private TimeSpan _period;
        private long _fired;

        public TrackedTimer(StoryClock clock, DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
        {
            _clock = clock;
            Schedule(now, dueTime, period);
        }

        public void Attach(ITimer inner) => _inner = inner;

        public void Fired()
        {
            lock (_gate)
            {
                _fired++;
            }
        }

        // The next occurrence not fired yet: the first for a one-shot timer, the n-th on the grid for a periodic one.
        public DateTimeOffset? NextDue()
        {
            lock (_gate)
            {
                if (_due is not { } due)
                {
                    return null;
                }

                if (_period <= TimeSpan.Zero || _period == Timeout.InfiniteTimeSpan)
                {
                    return _fired == 0 ? due : null;
                }

                return due + TimeSpan.FromTicks(_period.Ticks * _fired);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // The fake first: if it rejects the values, the tracker keeps the schedule that is really armed.
            var changed = _inner.Change(dueTime, period);
            Schedule(_clock.Fake.GetUtcNow(), dueTime, period);
            return changed;
        }

        public void Dispose()
        {
            _clock.Forget(this);
            _inner.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            _clock.Forget(this);
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        private void Schedule(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
                _period = period;
                _fired = 0;
            }
        }
    }
}
