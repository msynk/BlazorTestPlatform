namespace BlazorTestPlatform;

/// <summary>
/// Canned JavaScript-interop results. bUnit has no JavaScript engine, so components that call <c>IJSRuntime</c> always
/// get these results there. In real browsers (workbench under Server, WebAssembly or Auto) the real JavaScript keeps
/// running unless <see cref="InBrowsers"/> is set; then mocked identifiers answer with these results and every other
/// call still reaches the real JavaScript. Either way, a story that declares mocks has its JS calls recorded, so
/// <c>ExpectJSCall(...)</c> works under every driver.
/// </summary>
/// <example>
/// <code>
/// s.MockJS(js =>
/// {
///     js.Setup("localStorage.getItem", "dark", "theme");          // exact arguments
///     js.Setup("matchMedia.matches", true);                        // any arguments
///     js.SetupVoid("analytics.track");
///     js.Module("./_content/Charts/chart.js", chart => chart.SetupVoid("render"));
/// });
/// </code>
/// </example>
public sealed class JSInteropMocks
{
    private readonly List<JSInteropMock> _mocks = [];
    private readonly List<(string Module, JSInteropMocks Mocks)> _modules = [];

    /// <summary>
    /// Fail on JS calls without a setup instead of returning defaults (bUnit) or reaching the real JavaScript
    /// (browsers, with <see cref="InBrowsers"/>). Blazor's own <c>Blazor.*</c> calls are never affected. Default false.
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Also answer mocked calls in real browsers (workbench frames under Server, WebAssembly and Auto). Use it for
    /// browser APIs or third-party scripts whose real results would make the story non-deterministic. Default false:
    /// browsers run the real JavaScript and only record calls.
    /// </summary>
    public bool InBrowsers { get; set; }

    /// <summary>Sets <see cref="InBrowsers"/>.</summary>
    public JSInteropMocks ApplyInBrowsers(bool value = true)
    {
        InBrowsers = value;
        return this;
    }

    /// <summary>Setups in registration order.</summary>
    public IReadOnlyList<JSInteropMock> Mocks => _mocks;

    /// <summary>Module setups (<c>import("./module.js")</c>).</summary>
    public IReadOnlyList<(string Module, JSInteropMocks Mocks)> Modules => _modules;

    /// <summary>Returns <paramref name="result"/> for calls to <paramref name="identifier"/> (any arguments when none are given).</summary>
    public JSInteropMocks Setup<TResult>(string identifier, TResult result, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        _mocks.Add(new JSInteropMock<TResult>(identifier, result, arguments.Length == 0 ? null : arguments));
        return this;
    }

    /// <summary>Completes void calls to <paramref name="identifier"/> (any arguments when none are given).</summary>
    public JSInteropMocks SetupVoid(string identifier, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        _mocks.Add(new JSInteropVoidMock(identifier, arguments.Length == 0 ? null : arguments));
        return this;
    }

    /// <summary>Configures calls on a JavaScript module imported by the component.</summary>
    public JSInteropMocks Module(string moduleName, Action<JSInteropMocks> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentNullException.ThrowIfNull(configure);
        var module = new JSInteropMocks();
        configure(module);
        _modules.Add((moduleName, module));
        return this;
    }

    /// <summary>
    /// The setup that answers a call, or null. Later setups win; a setup without arguments matches any arguments; void
    /// setups answer only void calls and value setups only calls whose result type accepts their result.
    /// </summary>
    public JSInteropMock? Find(string identifier, IReadOnlyList<object?> arguments, Type? resultType)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(arguments);
        for (var i = _mocks.Count - 1; i >= 0; i--)
        {
            var mock = _mocks[i];
            if (!string.Equals(mock.Identifier, identifier, StringComparison.Ordinal)
                || (mock.Arguments is { } expected && !expected.SequenceEqual(arguments)))
            {
                continue;
            }

            if (resultType is null ? mock.ResultType is null : mock.ResultType is not null && resultType.IsAssignableFrom(mock.ResultType))
            {
                return mock;
            }
        }

        return null;
    }

    /// <summary>The setups of a module imported as <paramref name="moduleName"/>, or null (later registrations win).</summary>
    public JSInteropMocks? FindModule(string? moduleName)
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_modules[i].Module, moduleName, StringComparison.Ordinal))
            {
                return _modules[i].Mocks;
            }
        }

        return null;
    }

    internal void MergeFrom(JSInteropMocks other)
    {
        Strict |= other.Strict;
        InBrowsers |= other.InBrowsers;
        _mocks.AddRange(other._mocks);
        _modules.AddRange(other._modules);
    }
}

/// <summary>Visits mocks with their static result type (lets drivers register typed setups without reflection).</summary>
public interface IJSInteropMockVisitor
{
    /// <summary>A setup returning <typeparamref name="TResult"/>.</summary>
    void Visit<TResult>(string identifier, TResult result, IReadOnlyList<object?>? arguments);

    /// <summary>A void setup.</summary>
    void VisitVoid(string identifier, IReadOnlyList<object?>? arguments);
}

/// <summary>One JavaScript-interop setup.</summary>
/// <param name="Identifier">JS function identifier.</param>
/// <param name="Arguments">Exact arguments, or null for any.</param>
public abstract record JSInteropMock(string Identifier, IReadOnlyList<object?>? Arguments)
{
    /// <summary>Static result type, or null for a void setup.</summary>
    public abstract Type? ResultType { get; }

    /// <summary>The canned result (null for a void setup).</summary>
    public abstract object? BoxedResult { get; }

    /// <summary>Dispatches to the visitor with the static result type.</summary>
    public abstract void Accept(IJSInteropMockVisitor visitor);
}

/// <summary>A setup returning a value.</summary>
public sealed record JSInteropMock<TResult>(string Identifier, TResult Result, IReadOnlyList<object?>? Arguments) : JSInteropMock(Identifier, Arguments)
{
    /// <inheritdoc />
    public override Type? ResultType => typeof(TResult);

    /// <inheritdoc />
    public override object? BoxedResult => Result;

    /// <inheritdoc />
    public override void Accept(IJSInteropMockVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(Identifier, Result, Arguments);
    }
}

/// <summary>A void setup.</summary>
public sealed record JSInteropVoidMock(string Identifier, IReadOnlyList<object?>? Arguments) : JSInteropMock(Identifier, Arguments)
{
    /// <inheritdoc />
    public override Type? ResultType => null;

    /// <inheritdoc />
    public override object? BoxedResult => null;

    /// <inheritdoc />
    public override void Accept(IJSInteropMockVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.VisitVoid(Identifier, Arguments);
    }
}
