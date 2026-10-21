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
