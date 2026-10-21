using System.Text.Json;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BlazorTestPlatform.Workbench.Tests;

/// <summary>A scripted stand-in for the btp-driver.js module.</summary>
internal sealed class FakeDriverModule : IJSObjectReference
{
    public List<(string Identifier, object?[] Args)> Calls { get; } = [];

    public Func<string, object?[], object?> Handler { get; set; } = (_, _) => null;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add((identifier, args ?? []));
        var result = identifier == "settle" ? true : Handler(identifier, args ?? []);
        return ValueTask.FromResult((TValue)result!);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class InAppDriverTests
{
    private static async Task<(InAppStoryDriver Driver, FakeDriverModule Module, StoryContext Context)> CreateAsync(string storyId)
    {
        var story = StoryCatalog.FromAssemblyOf<InAppDriverTests>().Get(storyId);
        var scope = new StoryServiceScope();
        var render = new StoryRenderContext(story, new ServiceCollection().BuildServiceProvider(), HostingModel.InteractiveWebAssembly);
        await scope.ActivateAsync(story, HostingModel.InteractiveWebAssembly, render.Authentication);
        var module = new FakeDriverModule();
        var driver = new InAppStoryDriver(module, render, scope, "WebAssembly");
        return (driver, module, new StoryContext(story, driver, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Actions_are_sent_as_json_and_retried_until_actionable()
    {
        var (_, module, ctx) = await CreateAsync("tests-widget--plain");
        var attempts = 0;
        module.Handler = (id, args) => id == "performJson" && ++attempts < 3 ? "element is not visible" : null;

        await ctx.GetByRole("button", "Save", exact: true).ClickAsync();

        var perform = module.Calls.Where(c => c.Identifier == "performJson").ToArray();
        Assert.Equal(3, perform.Length);
        using var steps = JsonDocument.Parse((string)perform[0].Args[0]!);
        Assert.Equal("role", steps.RootElement[0].GetProperty("query").GetProperty("type").GetString());
        Assert.Equal("Save", steps.RootElement[0].GetProperty("query").GetProperty("name").GetString());
        Assert.True(steps.RootElement[0].GetProperty("query").GetProperty("exact").GetBoolean());
        Assert.Contains("\"kind\":\"click\"", (string)perform[0].Args[1]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unactionable_elements_fail_with_the_last_problem()
    {
        var (_, module, ctx) = await CreateAsync("tests-widget--plain");
        module.Handler = (id, _) => id == "performJson" ? "strict mode violation: 2 elements match" : null;

        var error = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByText("Go").ClickAsync(timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Contains("strict mode violation: 2 elements match", error.Message, StringComparison.Ordinal);
        var failed = Assert.Single(ctx.Steps);
        Assert.True(failed.Failed);
    }

    [Fact]
    public async Task Queries_and_assertions_read_element_snapshots()
    {
        var (_, module, ctx) = await CreateAsync("tests-widget--plain");
        module.Handler = (id, _) => id switch
        {
            "queryJson" => """[{"tagName":"output","role":"status","accessibleName":"","text":"3 items","isVisible":true,"isEnabled":true,"attributes":{"class":"count"}}]""",
            "focusedJson" => """{"tagName":"input","isFocused":true,"value":"Ada"}""",
            _ => null,
        };

        await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("3 items");
        await ctx.Expect(ctx.GetByRole("status")).ToHaveClassAsync("count");
        Assert.Equal("Ada", (await ctx.GetFocusedAsync())!.Value);
    }

    [Fact]
    public async Task Keyboard_calls_carry_kind_and_chord()
    {
        var (_, module, ctx) = await CreateAsync("tests-widget--plain");

        await ctx.ShiftTabAsync();
        await ctx.TypeAsync("hi");

        var keyboard = module.Calls.Where(c => c.Identifier == "keyboard").Select(c => $"{c.Args[0]}:{c.Args[1]}").ToArray();
        Assert.Equal(["press:Shift+Tab", "type:hi"], keyboard);
    }

    [Fact]
    public async Task Capabilities_fake_time_and_mocked_http_come_from_the_story()
    {
        var (plain, _, _) = await CreateAsync("tests-widget--plain");
        var (timed, _, timedContext) = await CreateAsync("tests-widget--fake-time");
        var (http, _, _) = await CreateAsync("tests-widget--http");

        Assert.False(plain.Capabilities.HasFlag(DriverCapabilities.FakeTime));
        Assert.True(timed.Capabilities.HasFlag(DriverCapabilities.FakeTime | DriverCapabilities.RealBrowser | DriverCapabilities.Layout));
        await timedContext.AdvanceTimeAsync(TimeSpan.FromHours(1));
        Assert.Empty(await http.GetHttpRequestsAsync(CancellationToken.None));
    }

    [Fact]
    public void Play_results_serialize_status_error_and_timeline()
    {
        var steps = new List<Results.StepRecord> { new() { Title = "play", Kind = Results.StepKind.Step, Children = [new() { Title = "click", Kind = Results.StepKind.Action, Error = "boom" }] } };

        using var json = JsonDocument.Parse(PlayResult.ToJson("failed", "boom", steps, 2, "Server"));

        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("boom", json.RootElement.GetProperty("error").GetString());
        Assert.Equal("Server", json.RootElement.GetProperty("renderer").GetString());
        Assert.Equal("click", json.RootElement.GetProperty("steps")[0].GetProperty("children")[0].GetProperty("title").GetString());
    }
}
