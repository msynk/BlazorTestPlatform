using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Core.Tests;

public sealed class AssertionTests
{
    private static readonly Story Story = StoryCatalog.FromAssemblyOf<AssertionTests>().Get("samples-greeter--default");

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task Negated_count_passes_whenever_the_count_differs(int matches, int unexpected)
    {
        var ctx = new StoryContext(Story, new FixedDriver(matches), TimeSpan.FromMilliseconds(50));

        await ctx.Expect(ctx.GetByRole("listitem")).Not.ToHaveCountAsync(unexpected);
    }

    [Fact]
    public async Task Negated_count_fails_when_the_count_matches()
    {
        var ctx = new StoryContext(Story, new FixedDriver(2), TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByRole("listitem")).Not.ToHaveCountAsync(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Negated_single_element_assertions_do_not_pass_on_missing_or_ambiguous_elements(int matches)
    {
        var ctx = new StoryContext(Story, new FixedDriver(matches), TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<StoryAssertionException>(() => ctx.Expect(ctx.GetByRole("listitem")).Not.ToHaveTextAsync("Other"));
    }

    public static TheoryData<string> NegatedStateAssertions => ["disabled", "enabled", "checked", "focused", "editable", "contain"];

    [Theory]
    [MemberData(nameof(NegatedStateAssertions))]
    public async Task Negated_state_assertions_fail_on_missing_or_ambiguous_elements(string assertion)
    {
        foreach (var matches in new[] { 0, 2 })
        {
            var ctx = new StoryContext(Story, new FixedDriver(matches), TimeSpan.FromMilliseconds(50));
            var not = ctx.Expect(ctx.GetByRole("listitem")).Not;
            Task Run() => assertion switch
            {
                "disabled" => not.ToBeDisabledAsync(),
                "enabled" => not.ToBeEnabledAsync(),
                "checked" => not.ToBeCheckedAsync(),
                "focused" => not.ToBeFocusedAsync(),
                "editable" => not.ToBeEditableAsync(),
                _ => not.ToContainTextAsync("zzz"),
            };

            var error = await Assert.ThrowsAsync<StoryAssertionException>(Run);
            Assert.Contains(matches == 0 ? "not found" : "strict mode violation", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Negated_state_assertions_pass_on_one_element_in_the_other_state()
    {
        var ctx = new StoryContext(Story, new FixedDriver(1), TimeSpan.FromMilliseconds(50));

        await ctx.Expect(ctx.GetByRole("listitem")).Not.ToBeDisabledAsync();
        await ctx.Expect(ctx.GetByRole("listitem")).Not.ToBeCheckedAsync();
        await ctx.Expect(ctx.GetByRole("listitem")).Not.ToContainTextAsync("zzz");
    }

    [Fact]
    public async Task Visibility_assertions_are_strict_but_a_missing_element_is_hidden()
    {
        var two = new StoryContext(Story, new FixedDriver(2), TimeSpan.FromMilliseconds(50));
        var none = new StoryContext(Story, new FixedDriver(0), TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<StoryAssertionException>(() => two.Expect(two.GetByRole("listitem")).ToBeVisibleAsync());
        await Assert.ThrowsAsync<StoryAssertionException>(() => two.Expect(two.GetByRole("listitem")).Not.ToBeVisibleAsync());
        await Assert.ThrowsAsync<StoryAssertionException>(() => two.Expect(two.GetByRole("listitem")).ToBeHiddenAsync());
        Assert.False(await two.GetByRole("listitem").IsVisibleAsync());

        await none.Expect(none.GetByRole("listitem")).ToBeHiddenAsync();
        await none.Expect(none.GetByRole("listitem")).Not.ToBeVisibleAsync();
        await Assert.ThrowsAsync<StoryAssertionException>(() => none.Expect(none.GetByRole("listitem")).ToBeVisibleAsync());
    }

    [Fact]
    public async Task Zero_times_waits_for_idle_like_the_not_form()
    {
        var driver = new FixedDriver(0);
        var ctx = new StoryContext(Story, driver, TimeSpan.FromMilliseconds(50));

        await ctx.ExpectAction("OnPing").ToHaveBeenCalledAsync(times: 0);

        Assert.Equal(1, driver.IdleWaits);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ctx.ExpectAction("OnPing").ToHaveBeenCalledAsync(times: -1));
    }

    [Fact]
    public void JS_arguments_format_collections_and_objects_by_content()
    {
        Assert.Equal("[1, 2, 3]", JSCallLog.Format(new[] { 1, 2, 3 }));
        Assert.NotEqual(JSCallLog.Format(new[] { 1, 2, 3 }), JSCallLog.Format(new[] { 9 }));
        Assert.Equal("[\"a, b\"]", JSCallLog.Format(new[] { "a, b" }));
        Assert.Equal("[\"a\", \"b\"]", JSCallLog.Format(new List<string> { "a", "b" }));
        Assert.Equal("{\"x\": 1, \"y\": [true, null]}", JSCallLog.Format(new Dictionary<string, object?> { ["x"] = 1, ["y"] = new object?[] { true, null } }));
        Assert.Equal("{\"width\":10,\"label\":\"w\"}", JSCallLog.Format(new Size(10, "w")));
        Assert.Equal("plain", JSCallLog.Format("plain"));
        Assert.Equal("1.5", JSCallLog.Format(1.5));
        Assert.Equal("true", JSCallLog.Format(true));
        Assert.Null(JSCallLog.Format(null));
    }

    private sealed record Size(int Width, string Label);

    [Fact]
    public void Failure_messages_end_with_the_timeline()
    {
        var result = new BlazorTestPlatform.Results.StoryResult
        {
            Key = "k", StoryId = "a--b", Title = "A", Name = "B", Driver = "bunit", Outcome = BlazorTestPlatform.Results.StoryOutcome.Failed, Error = "Expected role=dialog to be visible",
            Steps =
            [
                new BlazorTestPlatform.Results.StepRecord
                {
                    Title = "play",
                    Children = [new() { Title = "click role=button[name=\"Open\"]", StartMilliseconds = 3 }, new() { Title = "expect role=dialog to be visible", StartMilliseconds = 9, Error = "timeout" }],
                },
            ],
        };

        var message = new BlazorTestPlatform.Results.StoryFailedException(result).Message;

        Assert.Contains("Timeline:", message, StringComparison.Ordinal);
        Assert.Contains("ms   ✓ click role=button[name=\"Open\"]", message, StringComparison.Ordinal);
        Assert.Contains("ms   ✗ expect role=dialog to be visible", message, StringComparison.Ordinal);
    }

    private sealed class FixedDriver(int matches) : IStoryDriver
    {
        public string Name => "fixed";

        public DriverCapabilities Capabilities => DriverCapabilities.None;

        public HostingModel HostingModel => HostingModel.InMemory;

        public IServiceProvider? Services => null;

        public Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ElementInfo>>(Enumerable.Range(0, matches).Select(i => new ElementInfo { TagName = "li", Role = "listitem", Text = $"Item {i}" }).ToArray());

        public Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task KeyboardAsync(KeyboardAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken) => Task.FromResult<ElementInfo?>(null);

        public Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ActionRecord>>([]);

        public int IdleWaits { get; private set; }

        public Task WaitForIdleAsync(CancellationToken cancellationToken)
        {
            IdleWaits++;
            return Task.CompletedTask;
        }

        public Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetMarkupAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
