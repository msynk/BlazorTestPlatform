using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace BlazorTestPlatform.Core.Tests;

public sealed class JSInteropTests
{
    /// <summary>A WebAssembly-like runtime: in-process, answers "real" for strings.</summary>
    private sealed class InProcessRuntime : IJSInProcessRuntime
    {
        public List<string> Calls { get; } = [];

        [RequiresUnreferencedCode("test")]
        public TResult Invoke<TResult>(string identifier, params object?[]? args)
        {
            Calls.Add(identifier);
            return typeof(TResult) == typeof(string) ? (TResult)(object)"real" : default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(typeof(TValue) == typeof(string) ? (TValue)(object)"real" : default!);
        }
    }

    [Fact]
    public void Later_setups_win_and_result_types_must_fit()
    {
        var mocks = new JSInteropMocks()
            .Setup("theme", "light")
            .Setup("theme", "dark")
            .Setup("count", 3)
            .SetupVoid("track", "click");

        Assert.Equal("dark", mocks.Find("theme", [], typeof(string))?.BoxedResult);
        Assert.Null(mocks.Find("theme", [], typeof(int)));
        Assert.NotNull(mocks.Find("count", [], typeof(int?)));
        Assert.NotNull(mocks.Find("count", [], typeof(object)));
        Assert.NotNull(mocks.Find("track", ["click"], resultType: null));
        Assert.Null(mocks.Find("track", ["hover"], resultType: null));
        Assert.Null(mocks.Find("track", ["click"], typeof(string)));
    }

    [Fact]
    public void Modules_resolve_by_import_path_and_settings_merge()
    {
        var first = new JSInteropMocks().Module("./a.js", a => a.Setup("x", 1));
        var second = new JSInteropMocks { Strict = true }.ApplyInBrowsers().Module("./a.js", a => a.Setup("x", 2));
        var merged = new JSInteropMocks();
        merged.MergeFrom(new JSInteropMocks().Setup("y", 0));
        merged.MergeFrom(second);

        Assert.Equal(1, first.FindModule("./a.js")!.Find("x", [], typeof(int))!.BoxedResult);
        Assert.Null(first.FindModule("./b.js"));
        Assert.True(second.InBrowsers);
        Assert.True(merged.InBrowsers);
        Assert.True(merged.Strict);
        Assert.Equal(2, merged.FindModule("./a.js")!.Find("x", [], typeof(int))!.BoxedResult);
    }

    [Fact]
    public async Task Record_only_runtime_passes_everything_through_and_keeps_in_process_calls()
    {
        var inner = new InProcessRuntime();
        var log = new JSCallLog();
        var runtime = StoryJSRuntime.Create(inner, new JSInteropMocks().Setup("theme", "dark"), log, JSMockMode.RecordOnly);

        Assert.Equal("real", await runtime.InvokeAsync<string>("theme"));
        var inProcess = Assert.IsAssignableFrom<IJSInProcessRuntime>(runtime);
        Assert.Equal("real", inProcess.Invoke<string>("theme"));
        Assert.Equal(["theme", "theme"], inner.Calls);
        Assert.All(log.Calls, c => Assert.False(c.Mocked));
    }

    [Fact]
    public async Task Answer_mode_answers_in_process_calls_too()
    {
        var inner = new InProcessRuntime();
        var log = new JSCallLog();
        var runtime = (IJSInProcessRuntime)StoryJSRuntime.Create(inner, new JSInteropMocks().Setup("theme", "dark"), log, JSMockMode.Answer);

        Assert.Equal("dark", runtime.Invoke<string>("theme"));
        Assert.Equal("dark", await runtime.InvokeAsync<string>("theme"));
        Assert.Equal("real", runtime.Invoke<string>("other"));
        Assert.Equal(["other"], inner.Calls);
        Assert.Equal([true, true, false], log.Calls.Select(c => c.Mocked));
    }

    private sealed class FakeReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(JSMockMode.Answer)]
    [InlineData(JSMockMode.AnsweredByInner)]
    public async Task Property_reads_writes_and_constructors_are_mocked_and_recorded(JSMockMode mode)
    {
        var chart = new FakeReference();
        var inner = new InProcessRuntime();
        var log = new JSCallLog();
        var mocks = new JSInteropMocks()
            .SetupGet("document.title", "Story")
            .SetupSet("document.title")
            .Setup<IJSObjectReference>("new Chart", chart);
        var runtime = StoryJSRuntime.Create(inner, mocks, log, mode);

        Assert.Equal("Story", await runtime.GetValueAsync<string>("document.title"));
        await runtime.SetValueAsync("document.title", "Changed");
        Assert.Same(chart, await runtime.InvokeConstructorAsync("Chart", ["#canvas"]));

        Assert.Empty(inner.Calls);
        Assert.Equal(["get document.title", "set document.title", "new Chart"], log.Calls.Select(c => c.Identifier));
        Assert.All(log.Calls, c => Assert.True(c.Mocked));
        Assert.Equal(["Changed"], log.Calls[1].Arguments);
    }

    [Fact]
    public async Task Strict_mocks_reject_unmocked_property_reads()
    {
        var runtime = StoryJSRuntime.Create(new InProcessRuntime(), new JSInteropMocks { Strict = true }, new JSCallLog(), JSMockMode.Answer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.GetValueAsync<string>("navigator.language").AsTask());
        Assert.Contains("get navigator.language", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_process_property_reads_are_mocked()
    {
        var runtime = (IJSInProcessRuntime)StoryJSRuntime.Create(new InProcessRuntime(), new JSInteropMocks().SetupGet("innerWidth", 1280), new JSCallLog(), JSMockMode.Answer);

#pragma warning disable IL2026 // Test-only in-process call.
        Assert.Equal(1280, runtime.GetValue<int>("innerWidth"));
#pragma warning restore IL2026
    }

    [Fact]
    public async Task Missing_mocked_modules_are_returned_as_in_process_references_too()
    {
        var log = new JSCallLog();
        var runtime = StoryJSRuntime.Create(new ThrowingImportRuntime(), new JSInteropMocks().Module("./chart.js", m => m.Setup("size", 3)), log, JSMockMode.Answer);

        var module = await runtime.InvokeAsync<IJSInProcessObjectReference>("import", "./chart.js");

        Assert.NotNull(module);
#pragma warning disable IL2026 // Test-only in-process call.
        Assert.Equal(3, module.Invoke<int>("size"));
#pragma warning restore IL2026
    }

    /// <summary>A runtime whose module imports fail, as for a script the host does not ship.</summary>
    private sealed class ThrowingImportRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "import" ? throw new JSException("Failed to fetch dynamically imported module") : ValueTask.FromResult(default(TValue)!);
    }

    [Fact]
    public void Arguments_format_the_same_under_every_driver()
    {
        Assert.Null(JSCallLog.Format(null));
        Assert.Equal("true", JSCallLog.Format(true));
        Assert.Equal("1.5", JSCallLog.Format(1.5));
        Assert.Equal("42", JSCallLog.Format(42));
        Assert.Equal("text", JSCallLog.Format("text"));
    }
}
