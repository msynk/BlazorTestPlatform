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
    public ElementLocator GetByRole(string role, string? name = null, bool exact = false) => Get(By.Role(role, name, exact));

    /// <summary>Locates elements by text.</summary>
    public ElementLocator GetByText(string text, bool exact = false) => Get(By.Text(text, exact));

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
    public Task PressAsync(string key) =>
        Recorder.RunAsync($"press {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Press, key), CancellationToken));

    /// <summary>Types text into the focused element, one key at a time.</summary>
    public Task TypeAsync(string text) =>
        Recorder.RunAsync($"type \"{text}\"", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Type, text), CancellationToken));

    /// <summary>Holds a key down.</summary>
    public Task KeyDownAsync(string key) =>
        Recorder.RunAsync($"keydown {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Down, key), CancellationToken));

    /// <summary>Releases a key.</summary>
    public Task KeyUpAsync(string key) =>
        Recorder.RunAsync($"keyup {key}", StepKind.Keyboard, () => Driver.KeyboardAsync(new KeyboardAction(KeyboardActionKind.Up, key), CancellationToken));

    /// <summary>Moves focus forward with Tab.</summary>
    public async Task TabAsync(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await PressAsync(Keys.Tab).ConfigureAwait(false);
        }
    }

    /// <summary>Moves focus backward with Shift+Tab.</summary>
    public async Task ShiftTabAsync(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await PressAsync(Keys.Chord(Keys.Shift, Keys.Tab)).ConfigureAwait(false);
        }
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
        await Recorder.RunAsync("expect tab order", StepKind.Assertion, async () =>
        {
            for (var i = 0; i < expected.Length; i++)
            {
                await PressAsync(Keys.Tab).ConfigureAwait(false);
                await Expect(expected[i]).ToBeFocusedAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Records a named step that groups the actions/assertions inside it on the timeline.</summary>
    public Task StepAsync(string name, Func<Task> body) => Recorder.RunAsync(name, StepKind.Step, body);

    /// <summary>Waits until renders and in-flight work settle.</summary>
    public Task WaitForIdleAsync() => Recorder.RunAsync("wait for idle", StepKind.Wait, () => Driver.WaitForIdleAsync(CancellationToken));

    /// <summary>Advances the story's fake clock (requires <c>UseFakeTime</c> and <see cref="DriverCapabilities.FakeTime"/>).</summary>
    public Task AdvanceTimeAsync(TimeSpan delta)
    {
        Require(DriverCapabilities.FakeTime, "advancing fake time");
        return Recorder.RunAsync($"advance time {delta}", StepKind.Wait, () => Driver.AdvanceTimeAsync(delta, CancellationToken));
    }

    /// <summary>Auto-waiting assertions on requests answered by the story's <see cref="HttpMock"/>.</summary>
    /// <param name="method">HTTP method, or null for any.</param>
    /// <param name="pattern">URL pattern, as in <see cref="HttpMock.When(HttpMethod?, string)"/>.</param>
    public HttpRequestAssertions ExpectRequest(string? method, string pattern) => new(this, method, pattern);

    /// <summary>Requests answered by the story's <see cref="HttpMock"/>.</summary>
    public Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync() => Driver.GetHttpRequestsAsync(CancellationToken);

    /// <summary>
    /// Auto-waiting assertions on JavaScript-interop calls to <paramref name="identifier"/> (on the module imported as
    /// <paramref name="module"/>, when given). Requires the story to declare <c>MockJS(...)</c>, which records calls
    /// under every driver.
    /// </summary>
    public JSCallAssertions ExpectJSCall(string identifier, string? module = null) => new(this, identifier, module);

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

    internal Task PerformAsync(ElementLocator locator, ElementAction action, TimeSpan? timeout) =>
        Recorder.RunAsync($"{action.Describe()} {locator}", StepKind.Action, () => Driver.PerformAsync(locator.Chain, action, timeout ?? DefaultTimeout, CancellationToken));
}
