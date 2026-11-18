using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>
/// The API available to play functions. Every call is driver-agnostic, auto-waits, and is recorded on the
/// story timeline, so the same script runs in bUnit and in real browsers and explains its own failures.
/// </summary>
public sealed class StoryContext
{
    /// <summary>Creates a context for a driver.</summary>
    public StoryContext(Story story, IStoryDriver driver, TimeSpan? defaultTimeout = null, StepRecorder? recorder = null, CancellationToken cancellationToken = default)
    {
        Story = story ?? throw new ArgumentNullException(nameof(story));
        Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);
        Recorder = recorder ?? new StepRecorder();
        CancellationToken = cancellationToken;
        Random = new DeterministicRandom(story.Seed).Fork("play");
        Fake = new FakeData(Random.Fork("fake"));
        CheckAccessibilityAfterActions = story.ChecksAccessibilityAfterActions;
    }

    /// <summary>The story being played.</summary>
    public Story Story { get; }

    /// <summary>The driver executing the story.</summary>
    public IStoryDriver Driver { get; }

    /// <summary>Driver name (e.g. <c>bunit</c>, <c>chromium</c>).</summary>
    public string DriverName => Driver.Name;

    /// <summary>Driver capabilities.</summary>
    public DriverCapabilities Capabilities => Driver.Capabilities;

    /// <summary>Hosting model.</summary>
    public HostingModel HostingModel => Driver.HostingModel;

    /// <summary>In-process services (bUnit only); null in browsers.</summary>
    public IServiceProvider? Services => Driver.Services;

    /// <summary>Default auto-wait timeout for actions and assertions.</summary>
    public TimeSpan DefaultTimeout { get; set; }

    /// <summary>Cancellation for the whole story.</summary>
    public CancellationToken CancellationToken { get; }

    private readonly List<string> _softFailures = [];

    private readonly AsyncLocal<TimeSpan?> _assertionTimeout = new();

    /// <summary>Seeded random source for the play function.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Seeded fake data for the play function.</summary>
    public FakeData Fake { get; }

    /// <summary>Timeline recorder.</summary>
    public StepRecorder Recorder { get; }

    /// <summary>Recorded timeline.</summary>
    public IReadOnlyList<StepRecord> Steps => Recorder.Steps;

    /// <summary>Locates elements by query.</summary>
    public ElementLocator Get(By query) => new(this, LocatorChain.Start(query ?? throw new ArgumentNullException(nameof(query))));

    /// <summary>Locates elements by CSS selector.</summary>
    public ElementLocator Locate(string cssSelector) => Get(By.Css(cssSelector));

    /// <summary>Locates elements by ARIA role and accessible name.</summary>
    public ElementLocator GetByRole(string role, string? name = null, bool exact = false, System.Text.RegularExpressions.Regex? namePattern = null, bool? isChecked = null, bool? pressed = null, bool? expanded = null, bool? selected = null, bool? disabled = null, int? level = null, bool includeHidden = false) =>
        Get(By.Role(role, name, exact, namePattern, isChecked, pressed, expanded, selected, disabled, level, includeHidden));

    /// <summary>Locates elements by text.</summary>
    public ElementLocator GetByText(string text, bool exact = false) => Get(By.Text(text, exact));

    /// <summary>Elements whose text matches <paramref name="pattern"/> (innermost match).</summary>
    public ElementLocator GetByText(System.Text.RegularExpressions.Regex pattern) => Get(By.Text(pattern));

    /// <summary>Form controls whose label matches <paramref name="pattern"/>.</summary>
    public ElementLocator GetByLabel(System.Text.RegularExpressions.Regex pattern) => Get(By.Label(pattern));

    /// <summary>Inputs whose placeholder matches <paramref name="pattern"/>.</summary>
    public ElementLocator GetByPlaceholder(System.Text.RegularExpressions.Regex pattern) => Get(By.Placeholder(pattern));

    /// <summary>Images whose alt text matches <paramref name="pattern"/>.</summary>
    public ElementLocator GetByAltText(System.Text.RegularExpressions.Regex pattern) => Get(By.AltText(pattern));

    /// <summary>Elements whose title attribute matches <paramref name="pattern"/>.</summary>
    public ElementLocator GetByTitle(System.Text.RegularExpressions.Regex pattern) => Get(By.Title(pattern));

    /// <summary>Locates form controls by label.</summary>
    public ElementLocator GetByLabel(string text, bool exact = false) => Get(By.Label(text, exact));

    /// <summary>Locates inputs by placeholder.</summary>
    public ElementLocator GetByPlaceholder(string text, bool exact = false) => Get(By.Placeholder(text, exact));

    /// <summary>Locates elements by test id.</summary>
    public ElementLocator GetByTestId(string testId) => Get(By.TestId(testId));

    /// <summary>Locates images by alt text.</summary>
    public ElementLocator GetByAltText(string text, bool exact = false) => Get(By.AltText(text, exact));

    /// <summary>Locates elements by title attribute.</summary>
    public ElementLocator GetByTitle(string text, bool exact = false) => Get(By.Title(text, exact));

    /// <summary>Presses a key or chord on the focused element (e.g. <c>Enter</c>, <c>Shift+Tab</c>).</summary>
    public async Task PressAsync(string key)
    {
        await Recorder.RunAsync($"press {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Press, key), CancellationToken)).ConfigureAwait(false);
        await AfterActionAsync($"press {key}").ConfigureAwait(false);
    }

    /// <summary>Types text into the focused element, one key at a time.</summary>
    public async Task TypeAsync(string text)
    {
        await Recorder.RunAsync($"type \"{text}\"", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Type, text), CancellationToken)).ConfigureAwait(false);
        await AfterActionAsync($"type \"{text}\"").ConfigureAwait(false);
    }

    /// <summary>Holds a key down.</summary>
    public Task KeyDownAsync(string key) =>
        Recorder.RunAsync($"keydown {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Down, key), CancellationToken));

    /// <summary>Releases a key.</summary>
    public Task KeyUpAsync(string key) =>
        Recorder.RunAsync($"keyup {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Up, key), CancellationToken));

    /// <summary>Moves focus forward with Tab.</summary>
    public async Task TabAsync(int times = 1)
    {
        await WithoutActionChecksAsync(async () =>
        {
            for (var i = 0; i < times; i++)
            {
                await PressAsync(Keys.Tab).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await AfterActionAsync(times == 1 ? "press Tab" : $"press Tab {times} times").ConfigureAwait(false);
    }

    /// <summary>Moves focus backward with Shift+Tab.</summary>
    public async Task ShiftTabAsync(int times = 1)
    {
        var chord = Keys.Chord(Keys.Shift, Keys.Tab);
        await WithoutActionChecksAsync(async () =>
        {
            for (var i = 0; i < times; i++)
            {
                await PressAsync(chord).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await AfterActionAsync(times == 1 ? $"press {chord}" : $"press {chord} {times} times").ConfigureAwait(false);
    }

    /// <summary>The focused element, or null.</summary>
    public Task<ElementInfo?> GetFocusedAsync() => Driver.GetFocusedElementAsync(CancellationToken);

    /// <summary>Auto-waiting assertions on a locator.</summary>
    public LocatorAssertions Expect(ElementLocator locator) => new(this, locator ?? throw new ArgumentNullException(nameof(locator)), negate: false);

    /// <summary>Auto-waiting assertions on a query.</summary>
    public LocatorAssertions Expect(By query) => Expect(Get(query));

    /// <summary>Auto-waiting assertions on a recorded action (component callback).</summary>
    public ActionAssertions ExpectAction(string name) => new(this, name);

    /// <summary>Asserts the focus order produced by pressing Tab from the current focus.</summary>
    /// <param name="expected">Locators in expected order.</param>
    public async Task ExpectTabOrderAsync(params ElementLocator[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        await WithoutActionChecksAsync(() => Recorder.RunAsync("expect tab order", StepKind.Assertion, async () =>
        {
            for (var i = 0; i < expected.Length; i++)
            {
                await PressAsync(Keys.Tab).ConfigureAwait(false);
                await Expect(expected[i]).ToBeFocusedAsync().ConfigureAwait(false);
            }
        })).ConfigureAwait(false);
        await AfterActionAsync("the tab order").ConfigureAwait(false);
    }

    /// <summary>
    /// Soft assertions on <paramref name="locator"/>: a failure is recorded in the timeline and in
    /// <see cref="SoftFailures"/>, and the play script continues. The story still fails when play ends
    /// (<see cref="RunPlayAsync"/>), with every soft failure listed, as Playwright's <c>expect.soft</c>.
    /// </summary>
    public LocatorAssertions ExpectSoft(ElementLocator locator) => new(this, locator ?? throw new ArgumentNullException(nameof(locator)), negate: false, soft: true);

    /// <summary>Soft assertions on the elements <paramref name="query"/> finds (see <see cref="ExpectSoft(ElementLocator)"/>).</summary>
    public LocatorAssertions ExpectSoft(By query) => ExpectSoft(Get(query));

    /// <summary>
    /// Runs any assertion softly: a <see cref="StoryAssertionException"/> it throws is recorded in
    /// <see cref="SoftFailures"/> instead of stopping the play script
    /// (<c>await ctx.SoftAsync(() =&gt; ctx.ExpectAction("OnSave").ToHaveBeenCalledAsync())</c>). Skips, cancellation and
    /// other exceptions still propagate.
    /// </summary>
    public async Task SoftAsync(Func<Task> assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        try
        {
            await assertion().ConfigureAwait(false);
        }
        catch (StoryAssertionException ex)
        {
            lock (_softFailures)
            {
                _softFailures.Add(ex.Message);
            }
        }
    }

    /// <summary>Messages of the soft assertions that failed so far, in order.</summary>
    public IReadOnlyList<string> SoftFailures
    {
        get
        {
            lock (_softFailures)
            {
                return _softFailures.ToArray();
            }
        }
    }

    /// <summary>Throws one <see cref="StoryAssertionException"/> listing every failed soft assertion, if any.</summary>
    public void ThrowIfSoftFailures()
    {
        var failures = SoftFailures;
        if (failures.Count == 0)
        {
            return;
        }

        var message = new System.Text.StringBuilder()
            .Append(failures.Count == 1 ? "1 soft assertion failed:" : $"{failures.Count} soft assertions failed:");
        for (var i = 0; i < failures.Count; i++)
        {
            message.AppendLine().Append(i + 1).Append(". ").Append(failures[i].Replace("\n", "\n   ", StringComparison.Ordinal));
        }

        throw new StoryAssertionException(message.ToString());
    }

    /// <summary>
    /// Runs the story's play function (if any), then fails with every soft assertion that failed during it. Runners call
    /// this; a hard failure during play propagates unchanged (its soft failures stay visible as failed timeline steps).
    /// </summary>
    public async Task RunPlayAsync()
    {
        if (Story.Play is { } play)
        {
            await play(this).ConfigureAwait(false);
        }

        ThrowIfSoftFailures();
    }

    /// <summary>
    /// Retries <paramref name="assertion"/> until it completes without throwing, or fails with its last error after
    /// <paramref name="timeout"/> (default <see cref="DefaultTimeout"/>), as Playwright's <c>toPass</c>. Assertions inside
    /// make a single check per attempt unless they pass their own timeout, so the whole block shares one deadline. Only the
    /// last attempt's steps are kept in the timeline.
    /// </summary>
    /// <example>
    /// <code>
    /// await ctx.ExpectEventuallyAsync(async () =>
    /// {
    ///     await ctx.GetByRole("button", "Refresh").ClickAsync();
    ///     await ctx.Expect(ctx.GetByText("Ready")).ToBeVisibleAsync();
    /// });
    /// </code>
    /// </example>
    public Task ExpectEventuallyAsync(Func<Task> assertion, TimeSpan? timeout = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        var limit = timeout ?? DefaultTimeout;
        return Recorder.RunAsync($"expect eventually{(description is null ? string.Empty : " " + description)}", StepKind.Assertion, async () =>
        {
            var previous = _assertionTimeout.Value;
            _assertionTimeout.Value = TimeSpan.Zero;
            try
            {
                Exception? last = null;
                var (success, _) = await Poll.UntilAsync(
                    async _ =>
                    {
                        Recorder.ClearCurrentChildren();
                        try
                        {
                            await assertion().ConfigureAwait(false);
                            return (true, true);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException and not StorySkippedException)
                        {
                            last = ex;
                            return (false, false);
                        }
                    },
                    limit,
                    CancellationToken).ConfigureAwait(false);
                if (!success)
                {
                    throw new StoryAssertionException(
                        $"Expected {description ?? "the block"} to pass within {limit.TotalMilliseconds:0} ms.{Environment.NewLine}  Last error: {last?.Message}",
                        last!);
                }
            }
            finally
            {
                _assertionTimeout.Value = previous;
            }
        });
    }

    /// <summary>
    /// Polls <paramref name="probe"/> until <paramref name="predicate"/> accepts its value, as Playwright's
    /// <c>expect.poll</c>. Use it for state the DOM does not show (a service, a store, a mock):
    /// <c>await ctx.ExpectPollAsync(() =&gt; Task.FromResult(cart.Count), n =&gt; n == 3, "the cart to hold 3 items")</c>.
    /// </summary>
    public Task ExpectPollAsync<T>(Func<Task<T>> probe, Func<T, bool> predicate, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrEmpty(description);
        var limit = AssertionTimeout(timeout);
        return Recorder.RunAsync($"expect {description}", StepKind.Assertion, async () =>
        {
            var (success, last) = await Poll.UntilAsync(
                async _ =>
                {
                    var value = await probe().ConfigureAwait(false);
                    return (predicate(value), value);
                },
                limit,
                CancellationToken).ConfigureAwait(false);
            if (!success)
            {
                throw new StoryAssertionException($"Expected {description}.{Environment.NewLine}  Last value: {Format(last)} (waited {limit.TotalMilliseconds:0} ms)");
            }
        });
    }

    /// <summary>
    /// Checks that <paramref name="assertion"/> keeps passing for <paramref name="duration"/> (Kotest's
    /// <c>continually</c>), e.g. "the tooltip stays closed for 500 ms". When the story uses fake time and the driver can
    /// advance it, the duration is advanced on the story's clock in small steps, so the check is instant and
    /// deterministic; otherwise it waits in real time. Assertions inside make a single check each time.
    /// </summary>
    public Task ExpectToRemainAsync(Func<Task> assertion, TimeSpan duration, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Must be positive.");
        }

        return Recorder.RunAsync($"expect {description ?? "block"} to remain true for {duration.TotalMilliseconds:0} ms", StepKind.Assertion, async () =>
        {
            var previous = _assertionTimeout.Value;
            _assertionTimeout.Value = TimeSpan.Zero;
            try
            {
                var fakeTime = Supports(DriverCapabilities.FakeTime) && Story.FakeTimeStart is not null;
                var step = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, Math.Min(TimeSpan.FromMilliseconds(50).Ticks, duration.Ticks / 10)));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var elapsed = TimeSpan.Zero;
                while (true)
                {
                    Recorder.ClearCurrentChildren();
                    try
                    {
                        await assertion().ConfigureAwait(false);
                    }
                    catch (StoryAssertionException ex)
                    {
                        throw new StoryAssertionException(
                            $"Expected {description ?? "the block"} to remain true for {duration.TotalMilliseconds:0} ms, but it failed after {elapsed.TotalMilliseconds:0} ms.{Environment.NewLine}  {ex.Message}",
                            ex);
                    }

                    if (elapsed >= duration)
                    {
                        return;
                    }

                    var next = elapsed + step > duration ? duration - elapsed : step;
                    if (fakeTime)
                    {
                        await Driver.AdvanceTimeAsync(next, CancellationToken).ConfigureAwait(false);
                        elapsed += next;
                    }
                    else
                    {
                        await Task.Delay(next, CancellationToken).ConfigureAwait(false);
                        elapsed = clock.Elapsed;
                    }
                }
            }
            finally
            {
                _assertionTimeout.Value = previous;
            }
        });
    }

    /// <summary>
    /// The auto-wait timeout of an assertion: its own, else a single check inside <see cref="ExpectEventuallyAsync"/> and
    /// <see cref="ExpectToRemainAsync"/>, else <see cref="DefaultTimeout"/>.
    /// </summary>
    internal TimeSpan AssertionTimeout(TimeSpan? timeout) => timeout ?? _assertionTimeout.Value ?? DefaultTimeout;

    private static string Format<T>(T value) => value switch
    {
        null => "(null)",
        string text => $"\"{text}\"",
        System.Collections.IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(i => i?.ToString() ?? "null")) + "]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>Records a named step that groups the actions/assertions inside it on the timeline.</summary>
    public Task StepAsync(string name, Func<Task> body) => Recorder.RunAsync(name, StepKind.Step, body);

    /// <summary>
    /// Explores the component as a state machine (Hypothesis' rule-based testing, fast-check's model-based commands): takes
    /// <c>Steps</c> random actions among those possible at each point, chosen by a seed, and checks every invariant before
    /// the first step and after each one. A failure reports the steps, the seed that replays them and a play script to
    /// paste as a regression story. The seed derives from the story's, so runs repeat; <c>BTP_EXPLORE_SEED</c> tries others.
    /// </summary>
    /// <example>
    /// <code>
    /// await ctx.ExploreAsync("cart", e =&gt; e
    ///     .Action("add", c =&gt; c.GetByRole("button", "Add").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Add\").ClickAsync();")
    ///     .Action("remove", c =&gt; c.GetByRole("button", "Remove").ClickAsync(), when: c =&gt; c.GetByRole("button", "Remove").IsEnabledAsync())
    ///     .Invariant("count is never negative", c =&gt; c.Expect(c.GetByTestId("count")).Not.ToHaveTextAsync(new Regex("^-"))));
    /// </code>
    /// </example>
    public Task ExploreAsync(string name, Action<ExplorationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var exploration = new ExplorationBuilder();
        configure(exploration);
        return Exploration.RunAsync(this, name, exploration);
    }

    /// <summary>
    /// Records a named step that must finish within <paramref name="timeout"/> (Playwright's <c>test.step</c> timeout): a
    /// step that takes longer fails the story at that step, with its name, instead of using up the whole story's timeout.
    /// </summary>
    public Task StepAsync(string name, Func<Task> body, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (timeout <= TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Must be positive.");
        }

        return Recorder.RunAsync(name, StepKind.Step, async () =>
        {
            var running = body();
            try
            {
                await running.WaitAsync(timeout, CancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex) when (!running.IsCompleted)
            {
                // The body cannot be cancelled (play code takes no token): it is abandoned, and its later failure observed.
                _ = running.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                throw new StoryAssertionException($"Step '{name}' did not finish within {timeout.TotalMilliseconds:0} ms.", ex);
            }
        });
    }

    private readonly List<PlayAttachment> _attachments = [];

    /// <summary>Files the play script attached (<see cref="Attach(string, byte[], string?)"/>), in order.</summary>
    public IReadOnlyList<PlayAttachment> Attachments
    {
        get
        {
            lock (_attachments)
            {
                return _attachments.ToArray();
            }
        }
    }

    /// <summary>
    /// Attaches a file to the run (Playwright's <c>testInfo.attach</c>): runners write it with the run's artifacts, reports
    /// link it and the test framework packages attach it to the test result, whatever the outcome. The media type defaults
    /// from the name's extension.
    /// </summary>
    public void Attach(string name, byte[] content, string? mediaType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        lock (_attachments)
        {
            _attachments.Add(new PlayAttachment(name, content.ToArray(), mediaType ?? StoryFile.ContentTypeOf(name)));
        }

        Recorder.Log($"attach {name} ({content.Length} bytes)");
    }

    /// <summary>Attaches text (UTF-8) to the run; see <see cref="Attach(string, byte[], string?)"/>.</summary>
    public void Attach(string name, string text, string? mediaType = null) =>
        Attach(name, System.Text.Encoding.UTF8.GetBytes(text ?? throw new ArgumentNullException(nameof(text))), mediaType ?? (Path.HasExtension(name) ? null : "text/plain"));

    /// <summary>Waits until renders and in-flight work settle.</summary>
    public Task WaitForIdleAsync() => Recorder.RunAsync("wait for idle", StepKind.Wait, () => Driver.WaitForIdleAsync(CancellationToken));

    /// <summary>Advances the story's fake clock (requires <c>UseFakeTime</c> and <see cref="DriverCapabilities.FakeTime"/>).</summary>
    public Task AdvanceTimeAsync(TimeSpan delta)
    {
        Require(DriverCapabilities.FakeTime, "advancing fake time");
        return Recorder.RunAsync($"advance time {delta}", StepKind.Wait, () => Driver.AdvanceTimeAsync(delta, CancellationToken));
    }

    /// <summary>
    /// The policy <see cref="ExpectAccessibleAsync"/> evaluates with: runners set the run's policy (<c>btp.json</c> and run
    /// options); null uses <see cref="Configuration.BtpSettings.Current"/>.
    /// </summary>
    public Accessibility.AccessibilityPolicy? AccessibilityPolicy { get; set; }

    /// <summary>
    /// Scans the story now, in the middle of play, and fails when the accessibility policy fails (Compose's
    /// <c>enableAccessibilityChecks</c>, at the states you choose): an open menu, a dialog, a validation error, which the scan
    /// after play never sees. bUnit applies its markup rules and browsers run axe-core, with the story's disabled rules and
    /// waivers; drivers that cannot scan (the in-app driver) record the check as skipped. Waits for renders to settle first.
    /// </summary>
    /// <param name="label">Names the state in the timeline and the failure (<c>"menu open"</c>).</param>
    public Task ExpectAccessibleAsync(string? label = null) =>
        Recorder.RunAsync(label is null ? "expect accessible" : $"expect accessible ({label})", StepKind.Check, async () =>
        {
            await Driver.WaitForIdleAsync(CancellationToken).ConfigureAwait(false);
            var policy = AccessibilityPolicy ?? Configuration.BtpSettings.Current.Accessibility;
            Accessibility.AccessibilityReport report;
            try
            {
                report = await Driver.ScanAccessibilityAsync(policy, Story.Accessibility, CancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                Recorder.Log($"accessibility check skipped: {ex.Message}");
                return;
            }

            var evaluation = policy.Evaluate(report, Story.Id, Story.Accessibility);
            if (!evaluation.Passed)
            {
                throw new Accessibility.AccessibilityViolationException(evaluation, label is null ? $"{Story.Id} during play" : $"{Story.Id} ({label})");
            }
        });

    private readonly List<VisualResult> _screenshots = [];

    /// <summary>The named screenshots compared so far (<see cref="ExpectScreenshotAsync"/>), in order.</summary>
    public IReadOnlyList<VisualResult> Screenshots
    {
        get
        {
            lock (_screenshots)
            {
                return _screenshots.ToArray();
            }
        }
    }

    /// <summary>
    /// Compares a screenshot of the current state with a committed baseline, as Playwright's <c>toHaveScreenshot('name')</c>:
    /// the open menu, the error state, the second step of a wizard, next to the story's own snapshot. The baseline is
    /// <c>{story-id}/{viewport}.{theme}.{browser}-{platform}.{name}.png</c>, compared with the story's tolerances and masks
    /// (and <c>data-btp-ignore</c>), written when missing locally and approved with <c>btp approve</c> like any other.
    /// Captures the story root, or the element <paramref name="target"/> finds, or the page with <paramref name="fullPage"/>.
    /// Drivers that cannot take screenshots (bUnit, the in-app driver) record the check as skipped and the script goes on.
    /// </summary>
    /// <param name="name">Unique within the story, such as <c>menu-open</c>; <c>initial</c> is reserved.</param>
    /// <param name="target">An element to capture instead of the story root.</param>
    /// <param name="fullPage">Capture the whole page (for overlays rendered outside the story root); null follows the story's <c>Visual(v =&gt; v.FullPage = ...)</c>.</param>
    /// <remarks>An explicit call is compared even when the story takes no snapshot of its own (<c>NoVisual()</c>).</remarks>
    public Task ExpectScreenshotAsync(string name, ElementLocator? target = null, bool? fullPage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var slug = StoryId.Slugify(name);
        if (slug.Length == 0 || string.Equals(slug, "initial", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' cannot name a screenshot: use letters or digits, and not 'initial' (the snapshot taken before play).", nameof(name));
        }

        lock (_screenshots)
        {
            if (!_screenshotNames.Add(slug))
            {
                throw new ArgumentException($"The story already took a screenshot named '{name}': each one needs its own name (its own baseline).", nameof(name));
            }
        }

        return Recorder.RunAsync($"expect screenshot \"{name}\"{(target is null ? string.Empty : $" of {target}")}", StepKind.Check, async () =>
        {
            if (!Supports(DriverCapabilities.Screenshots))
            {
                Recorder.Log($"screenshot skipped: the {DriverName} driver cannot take screenshots");
                return;
            }

            await Driver.WaitForIdleAsync(CancellationToken).ConfigureAwait(false);
            VisualResult result;
            try
            {
                result = await Driver.MatchScreenshotAsync(name, target?.Chain, fullPage, CancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                Recorder.Log($"screenshot skipped: {ex.Message}");
                return;
            }

            lock (_screenshots)
            {
                _screenshots.Add(result);
            }

            if (result.IsFailure)
            {
                throw new StoryAssertionException(result.Status switch
                {
                    VisualStatus.MissingBaseline => $"Missing visual baseline '{result.SnapshotName}'. Run locally (or set BTP_UPDATE_SNAPSHOTS=missing) to create it, then commit it. Actual: {result.ActualPath}",
                    VisualStatus.SizeMismatch => $"Screenshot '{result.SnapshotName}' changed size ({result.Width}x{result.Height}). Diff: {result.DiffPath}. Approve with 'btp approve' if intended.",
                    _ => $"Screenshot '{result.SnapshotName}' differs by {result.DiffPixels} px ({result.DiffRatio:P2}). Diff: {result.DiffPath}. Approve with 'btp approve' if intended.",
                } + (result.Note is null ? string.Empty : " " + result.Note));
            }
        });
    }

    private readonly HashSet<string> _screenshotNames = new(StringComparer.Ordinal);

    /// <summary>The story's accessibility tree as text (roles, names and states; see <see cref="ExpectAriaSnapshotAsync"/>).</summary>
    public Task<string> GetAriaSnapshotAsync() => Driver.GetAriaSnapshotAsync(CancellationToken);

    /// <summary>
    /// Waits until the story's accessibility tree matches <paramref name="expected"/>: one line per element with a role
    /// (generic containers flattened), as <c>- role "name" [state]</c>, children indented by two spaces under a line
    /// ending in <c>:</c>. Indentation common to all lines, blank lines and trailing spaces are ignored. On failure the
    /// message shows the actual tree, ready to paste. Equal under bUnit, Playwright and the in-app driver.
    /// <para>
    /// Names can be patterns (<c>- link /Order \d+/</c>). With <see cref="AriaSnapshotMatch.Contains"/> the expected tree only
    /// has to be part of the actual one (Playwright's partial matching): leave out nodes, names and states you do not care
    /// about, and add <c>- /children: equal</c> under a node whose children must all be listed. See <see cref="AriaSnapshotTemplate"/>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// await ctx.ExpectAriaSnapshotAsync("""
    ///     - heading "Sign in" [level=2]
    ///     - textbox "Email"
    ///     - button "Continue" [disabled]
    ///     """);
    /// </code>
    /// </example>
    /// <param name="expected">The expected tree.</param>
    /// <param name="timeout">How long to wait for the tree to match.</param>
    /// <param name="match">Exact, or contained in the actual tree.</param>
    /// <param name="sourceFile">Filled in by the compiler: where the call is, so <c>btp snapshots update-source</c> can rewrite the expected tree.</param>
    /// <param name="sourceLine">Filled in by the compiler.</param>
    public Task ExpectAriaSnapshotAsync(string expected, TimeSpan? timeout = null, AriaSnapshotMatch match = AriaSnapshotMatch.Exact,
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "", [System.Runtime.CompilerServices.CallerLineNumber] int sourceLine = 0)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wanted = NormalizeSnapshot(expected);
        AriaSnapshotTemplate.Validate(wanted);
        return Recorder.RunAsync(match == AriaSnapshotMatch.Exact ? "expect accessibility tree to match" : "expect accessibility tree to contain", StepKind.Assertion, async () =>
        {
            var (success, actual) = await Poll.UntilAsync(
                async ct =>
                {
                    var snapshot = NormalizeSnapshot(await Driver.GetAriaSnapshotAsync(ct).ConfigureAwait(false));
                    return (AriaSnapshotTemplate.Matches(snapshot, wanted, match), snapshot);
                },
                AssertionTimeout(timeout),
                CancellationToken).ConfigureAwait(false);
            if (!success)
            {
                if (sourceFile.Length > 0 && sourceLine > 0)
                {
                    lock (_ariaMismatches)
                    {
                        _ariaMismatches.Add(new AriaSnapshotMismatch { SourceFile = sourceFile, SourceLine = sourceLine, Expected = wanted, Actual = actual, Match = match });
                    }
                }

                throw new StoryAssertionException(
                    $"Expected the accessibility tree to {(match == AriaSnapshotMatch.Exact ? "match" : "contain")}.{Environment.NewLine}Expected:{Environment.NewLine}{Indent(wanted)}{Environment.NewLine}" +
                    $"Actual:{Environment.NewLine}{Indent(actual)}{Environment.NewLine}Driver: {Driver.Name}");
            }
        });

        static string Indent(string text) => string.Join(Environment.NewLine, text.Split('\n').Select(l => "  " + l));
    }

    private readonly List<AriaSnapshotMismatch> _ariaMismatches = [];

    /// <summary>The accessibility snapshots that did not match, with their call sites (for <c>btp snapshots update-source</c>).</summary>
    public IReadOnlyList<AriaSnapshotMismatch> AriaSnapshotMismatches
    {
        get
        {
            lock (_ariaMismatches)
            {
                return _ariaMismatches.ToArray();
            }
        }
    }

    // Same rules as AriaSnapshot.Normalize: line endings, blank lines, trailing spaces and common indentation.
    internal static string NormalizeSnapshot(string snapshot)
    {
        var lines = snapshot.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();
        var indent = lines.Length == 0 ? 0 : lines.Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l[indent..]));
    }

    /// <summary>
    /// Takes the page offline (<c>navigator.onLine</c> turns false, requests fail) or back online. Requires
    /// <see cref="DriverCapabilities.BrowserEmulation"/>; other drivers report the story as skipped. Under Interactive
    /// Server the circuit's connection is cut too, so offline behaviour is best tested under WebAssembly.
    /// </summary>
    public Task SetOfflineAsync(bool offline = true)
    {
        Require(DriverCapabilities.BrowserEmulation, "emulating offline mode");
        return Recorder.RunAsync(offline ? "go offline" : "go online", StepKind.Action, () => Driver.SetOfflineAsync(offline, CancellationToken));
    }

    /// <summary>
    /// Pauses the story's Blazor Server circuit and resumes it, as when a user leaves a tab long enough for the browser to
    /// release it and then comes back (.NET 10's <c>Blazor.pauseCircuit()</c> and <c>Blazor.resumeCircuit()</c>). The
    /// server persists the state of <c>[PersistentState]</c> properties when it pauses; on resume a new circuit renders the
    /// components again from that state, so anything not persisted starts over. Check what the user sees afterwards:
    /// <code>await ctx.PauseAndResumeCircuitAsync(); await ctx.Expect(ctx.GetByTestId("draft")).ToHaveTextAsync("Saved: 2");</code>
    /// Requires <see cref="DriverCapabilities.CircuitControl"/> (browsers under Interactive Server); other drivers and
    /// render modes report the story as skipped. Recorded actions, JS calls and requests start over with the new circuit.
    /// </summary>
    public Task PauseAndResumeCircuitAsync()
    {
        Require(DriverCapabilities.CircuitControl, "pausing and resuming the circuit");
        return Recorder.RunAsync("pause and resume the circuit", StepKind.Action, async () =>
        {
            await Driver.PauseAndResumeCircuitAsync(CancellationToken).ConfigureAwait(false);
            await Driver.WaitForIdleAsync(CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Changes the position <c>navigator.geolocation</c> reports (null: no position) and grants the <c>geolocation</c>
    /// permission. Requires <see cref="DriverCapabilities.BrowserEmulation"/>; other drivers report the story as skipped.
    /// </summary>
    public Task SetGeolocationAsync(GeoPosition? position)
    {
        Require(DriverCapabilities.BrowserEmulation, "emulating geolocation");
        var validated = position?.Validated();
        return Recorder.RunAsync(validated is null ? "clear geolocation" : $"set geolocation {validated.Latitude}, {validated.Longitude}", StepKind.Action,
            () => Driver.SetGeolocationAsync(validated, CancellationToken));
    }

    /// <summary>Auto-waiting assertions on requests answered by the story's <see cref="HttpMock"/>.</summary>
    /// <param name="method">HTTP method, or null for any.</param>
    /// <param name="pattern">URL pattern, as in <see cref="HttpMock.When(HttpMethod?, string)"/>.</param>
    public HttpRequestAssertions ExpectRequest(string? method, string pattern) => new(this, method, pattern);

    /// <summary>
    /// Assertions on the navigations the story asked for, by target: a path relative to the app (<c>/orders/42</c>, with
    /// <c>*</c> wildcards; the query is compared only when the pattern has one) or an absolute URL.
    /// <c>await ctx.ExpectNavigation("/orders/*").ToHaveNavigatedAsync()</c>. In browsers the workbench stops navigations
    /// that would leave the story frame, so the story stays on screen after the assertion.
    /// </summary>
    public NavigationAssertions ExpectNavigation(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        return NavigationAssertions.For(this, pattern);
    }

    /// <summary>Assertions on navigations whose target, as <c>/relative/path?query#fragment</c>, matches <paramref name="pattern"/>.</summary>
    public NavigationAssertions ExpectNavigation(System.Text.RegularExpressions.Regex pattern) =>
        NavigationAssertions.For(this, pattern ?? throw new ArgumentNullException(nameof(pattern)));

    /// <summary>The navigations the story asked for so far.</summary>
    public Task<IReadOnlyList<NavigationRecord>> GetNavigationsAsync() => Driver.GetNavigationsAsync(CancellationToken);

    /// <summary>Requests answered by the story's <see cref="HttpMock"/>.</summary>
    public Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync() => Driver.GetHttpRequestsAsync(CancellationToken);

    /// <summary>
    /// Auto-waiting assertions on JavaScript-interop calls to <paramref name="identifier"/> (on the module imported as
    /// <paramref name="module"/>, when given). Requires the story to declare <c>MockJS(...)</c>, which records calls
    /// under every driver.
    /// </summary>
    public JSCallAssertions ExpectJSCall(string identifier, string? module = null) => new(this, identifier, module);

    /// <summary>
    /// Plays the JavaScript side of interop: calls the <c>[JSInvokable]</c> <paramref name="method"/> of the
    /// <c>DotNetObjectReference</c> the component passed in its latest call to <paramref name="identifier"/>, with
    /// <paramref name="arguments"/>, on the story's renderer, then waits for it to settle. Use it for components that wrap
    /// a JavaScript widget or observer: <c>await ctx.InvokeDotNetAsync("resizeObserver.observe", "OnResize", 320, 200)</c>.
    /// The call must be recorded, so mock it (<c>MockJS</c>). Works in bUnit, Playwright and the in-app driver; returns
    /// the method's result as JSON (null for none).
    /// </summary>
    public async Task<string?> InvokeDotNetAsync(string identifier, string method, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(arguments);
        var json = DotNetCallbacks.ArgumentsToJson(arguments);
        return await Recorder.RunAsync($"invoke .NET {method} via {identifier}", StepKind.Action, async () =>
        {
            var calls = await Driver.GetJSCallsAsync(CancellationToken).ConfigureAwait(false);
            var call = calls.LastOrDefault(c => string.Equals(c.Identifier, identifier, StringComparison.Ordinal) && c.DotNetReferences > 0)
                ?? throw new StoryAssertionException(
                    $"No call to {identifier} passed a DotNetObjectReference.{Environment.NewLine}  Calls: {(calls.Count == 0 ? "none (mock the call with MockJS so it is recorded)" : string.Join(", ", calls.Select(c => c.ToString())))}");
            var result = await Driver.InvokeDotNetAsync(call.Sequence, 0, method, json, CancellationToken).ConfigureAwait(false);
            await Driver.WaitForIdleAsync(CancellationToken).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    /// <summary>JavaScript-interop calls recorded for the story (stories that declare <c>MockJS(...)</c>).</summary>
    public Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync() => Driver.GetJSCallsAsync(CancellationToken);

    /// <summary>Recorded callback invocations.</summary>
    public Task<IReadOnlyList<ActionRecord>> GetActionsAsync() => Driver.GetActionsAsync(CancellationToken);

    /// <summary>Current markup of the story root.</summary>
    public Task<string> GetMarkupAsync() => Driver.GetMarkupAsync(CancellationToken);

    /// <summary>Adds a message to the timeline.</summary>
    public void Log(string message) => Recorder.Log(message);

    /// <summary>True when the driver has all the capabilities.</summary>
    public bool Supports(DriverCapabilities capabilities) => (Capabilities & capabilities) == capabilities;

    /// <summary>Skips the rest of the story (reported as skipped for this driver) unless the capabilities are present.</summary>
    public void Require(DriverCapabilities capabilities, string? reason = null)
    {
        if (!Supports(capabilities))
        {
            throw new StorySkippedException(
                $"Driver '{DriverName}' lacks {capabilities & ~Capabilities}{(reason is null ? string.Empty : $" required for {reason}")}.");
        }
    }

    internal Task ForcePseudoStatesAsync(ElementLocator locator, PseudoState states)
    {
        Require(DriverCapabilities.Css, "forcing CSS pseudo-classes");
        var shown = states == PseudoState.None ? "clear forced states of" : $"force {string.Join(", ", Enum.GetValues<PseudoState>().Where(s => s != PseudoState.None && states.HasFlag(s)).Select(PseudoClass))} on";
        return Recorder.RunAsync($"{shown} {locator}", StepKind.Action, async () =>
        {
            await Driver.ForcePseudoStatesAsync(locator.Chain, states, CancellationToken).ConfigureAwait(false);
            await Driver.WaitForIdleAsync(CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>The CSS name of a single <see cref="PseudoState"/> (<c>:focus-visible</c>).</summary>
    public static string PseudoClass(PseudoState state) => state switch
    {
        PseudoState.Hover => ":hover",
        PseudoState.Active => ":active",
        PseudoState.Focus => ":focus",
        PseudoState.FocusVisible => ":focus-visible",
        PseudoState.FocusWithin => ":focus-within",
        PseudoState.Visited => ":visited",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Pass a single state."),
    };

    internal async Task PerformAsync(ElementLocator locator, ElementAction action, TimeSpan? timeout)
    {
        var title = $"{action.Describe()} {locator}";
        await Recorder.RunAsync(title, StepKind.Action, () => Driver.PerformAsync(locator.Chain, action, timeout ?? DefaultTimeout, CancellationToken)).ConfigureAwait(false);
        await AfterActionAsync(title).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks accessibility after every element action and key press from here on (true) or stops (false). Starts as the
    /// story's <c>CheckAccessibilityAfterActions()</c>; stories that skip accessibility checks are never checked.
    /// </summary>
    public bool CheckAccessibilityAfterActions { get; set; }

    private Task AfterActionAsync(string title) =>
        CheckAccessibilityAfterActions && _actionChecksPaused == 0 && !Story.Accessibility.Skip ? ExpectAccessibleAsync("after " + title) : Task.CompletedTask;

    // Tab loops (TabAsync, ShiftTabAsync, ExpectTabOrderAsync) check once at the end, not after every key press.
    private int _actionChecksPaused;

    private async Task WithoutActionChecksAsync(Func<Task> body)
    {
        _actionChecksPaused++;
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            _actionChecksPaused--;
        }
    }
}
