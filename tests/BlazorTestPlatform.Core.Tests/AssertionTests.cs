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

        public Task WaitForIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetMarkupAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
