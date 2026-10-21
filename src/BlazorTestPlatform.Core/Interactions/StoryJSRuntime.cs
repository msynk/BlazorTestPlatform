using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace BlazorTestPlatform;

/// <summary>Who answers the calls a <see cref="StoryJSRuntime"/> sees.</summary>
public enum JSMockMode
{
    /// <summary>The real runtime answers every call; calls are only recorded (browsers without <see cref="JSInteropMocks.InBrowsers"/>).</summary>
    RecordOnly,

    /// <summary>Mocked identifiers are answered by the wrapper, every other call reaches the real runtime (browsers with <see cref="JSInteropMocks.InBrowsers"/>).</summary>
    Answer,

    /// <summary>The inner runtime already answers the mocks (bUnit's own setups); calls are recorded and flagged as mocked when a setup matches.</summary>
    AnsweredByInner,
}

/// <summary>
/// An <see cref="IJSRuntime"/> decorator that records a story's JavaScript-interop calls and, when asked, answers the
/// story's <see cref="JSInteropMocks"/> before the call reaches the real runtime. Drivers wrap the runtime the story's
/// components inject with it; Blazor's own runtime (rendering, navigation, focus) is never wrapped.
/// </summary>
public class StoryJSRuntime : IJSRuntime
{
    internal const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;

    internal const string InProcessMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed.";

    private const string ImportIdentifier = "import";

    private protected StoryJSRuntime(IJSRuntime inner, JSInteropMocks mocks, JSCallLog log, JSMockMode mode)
    {
        Inner = inner;
        Mocks = mocks;
        Log = log;
        Mode = mode;
    }

    /// <summary>The wrapped runtime.</summary>
    public IJSRuntime Inner { get; }

    /// <summary>The story's mocks.</summary>
    public JSInteropMocks Mocks { get; }

    /// <summary>Where calls are recorded.</summary>
    public JSCallLog Log { get; }

    /// <summary>Who answers mocked calls.</summary>
    public JSMockMode Mode { get; }

    /// <summary>
    /// Wraps <paramref name="inner"/>. The result implements <see cref="IJSInProcessRuntime"/> when the inner runtime does
    /// (WebAssembly), so components that cast to it keep working.
    /// </summary>
    public static IJSRuntime Create(IJSRuntime inner, JSInteropMocks mocks, JSCallLog log, JSMockMode mode)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(mocks);
        ArgumentNullException.ThrowIfNull(log);
        return inner is IJSInProcessRuntime inProcess
            ? new InProcessStoryJSRuntime(inProcess, mocks, log, mode)
            : new StoryJSRuntime(inner, mocks, log, mode);
    }

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, object?[]? args) =>
        InvokeCoreAsync<TValue>(identifier, args, CancellationToken.None, hasToken: false);

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeCoreAsync<TValue>(identifier, args, cancellationToken, hasToken: true);

    /// <inheritdoc />
    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args)
    {
        Log.Add("new " + identifier, null, args, mocked: false);
        return Inner.InvokeConstructorAsync(identifier, args);
    }

    /// <inheritdoc />
    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Log.Add("new " + identifier, null, args, mocked: false);
        return Inner.InvokeConstructorAsync(identifier, cancellationToken, args);
    }

    /// <inheritdoc />
    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier)
    {
        Log.Add("get " + identifier, null, null, mocked: false);
        return Inner.GetValueAsync<TValue>(identifier);
    }

    /// <inheritdoc />
    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken)
    {
        Log.Add("get " + identifier, null, null, mocked: false);
        return Inner.GetValueAsync<TValue>(identifier, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, TValue value)
    {
        Log.Add("set " + identifier, null, [value], mocked: false);
        return Inner.SetValueAsync(identifier, value);
    }

    /// <inheritdoc />
    public ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, TValue value, CancellationToken cancellationToken)
    {
        Log.Add("set " + identifier, null, [value], mocked: false);
        return Inner.SetValueAsync(identifier, value, cancellationToken);
    }

    /// <summary>True for Blazor's own interop (<c>Blazor._internal...</c>), which mocks and strict mode never touch.</summary>
    internal static bool IsFrameworkCall(string identifier) => identifier.StartsWith("Blazor.", StringComparison.Ordinal);

    /// <summary>Records a call and answers it from the mocks when the mode says so; throws for unmocked calls in strict mode.</summary>
    internal static bool TryAnswer<TValue>(JSInteropMocks? mocks, JSMockMode mode, bool strict, string identifier, string? module, object?[] args, JSCallLog log, out TValue result)
    {
        result = default!;
        if (mocks is null || mode == JSMockMode.RecordOnly || IsFrameworkCall(identifier))
        {
            log.Add(identifier, module, args, mocked: false);
            return false;
        }

        var isVoid = typeof(TValue) == typeof(IJSVoidResult);
        var mock = mocks.Find(identifier, args, isVoid ? null : typeof(TValue));
        if (mode == JSMockMode.AnsweredByInner)
        {
            log.Add(identifier, module, args, mocked: mock is not null);
            return false;
        }

        if (mock is not null)
        {
            log.Add(identifier, module, args, mocked: true);
            result = mock.BoxedResult is null ? default! : (TValue)mock.BoxedResult;
            return true;
        }

        log.Add(identifier, module, args, mocked: false);
        if (strict)
        {
            var target = module is null ? identifier : module + ":" + identifier;
            throw new InvalidOperationException(
                $"The JS call '{target}' has no setup and the story's JS mocks are strict. Add js.Setup(\"{identifier}\", ...) or js.SetupVoid(\"{identifier}\") to the story's MockJS(...).");
        }

        return false;
    }

    private protected async ValueTask<TValue> InvokeCoreAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, object?[]? args, CancellationToken cancellationToken, bool hasToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        args ??= [];
        if (identifier == ImportIdentifier && args is [string path, ..])
        {
            return await ImportAsync<TValue>(path, args, cancellationToken, hasToken).ConfigureAwait(false);
        }

        if (TryAnswer<TValue>(Mocks, Mode, Mocks.Strict, identifier, null, args, Log, out var mocked))
        {
            return mocked;
        }

        return hasToken
            ? await Inner.InvokeAsync<TValue>(identifier, cancellationToken, args).ConfigureAwait(false)
            : await Inner.InvokeAsync<TValue>(identifier, args).ConfigureAwait(false);
    }

    private async ValueTask<TValue> ImportAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string path, object?[] args, CancellationToken cancellationToken, bool hasToken)
    {
        var moduleMocks = Mode == JSMockMode.RecordOnly ? null : Mocks.FindModule(path);
        Log.Add(ImportIdentifier, null, args, mocked: moduleMocks is not null);

        TValue? real = default;
        try
        {
            real = hasToken
                ? await Inner.InvokeAsync<TValue>(ImportIdentifier, cancellationToken, args).ConfigureAwait(false)
                : await Inner.InvokeAsync<TValue>(ImportIdentifier, args).ConfigureAwait(false);
        }
        catch (JSException) when (moduleMocks is not null && Mode == JSMockMode.Answer)
        {
            // A mocked module does not have to exist (e.g. a script from a package the workbench does not ship).
        }

        var strict = Mocks.Strict || moduleMocks?.Strict == true;
        object? wrapped = real switch
        {
            IJSInProcessObjectReference inProcess => new InProcessStoryJSObjectReference(inProcess, path, moduleMocks, Mode, strict, Log),
            IJSObjectReference reference => new StoryJSObjectReference(reference, path, moduleMocks, Mode, strict, Log),
            null when moduleMocks is not null && Mode == JSMockMode.Answer && typeof(TValue).IsAssignableFrom(typeof(StoryJSObjectReference))
                => new StoryJSObjectReference(null, path, moduleMocks, Mode, strict, Log),
            _ => null,
        };

        return wrapped is TValue typed ? typed : real!;
    }
}

/// <summary><see cref="StoryJSRuntime"/> over an in-process (WebAssembly) runtime.</summary>
internal sealed class InProcessStoryJSRuntime(IJSInProcessRuntime inner, JSInteropMocks mocks, JSCallLog log, JSMockMode mode)
    : StoryJSRuntime(inner, mocks, log, mode), IJSInProcessRuntime
{
    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public TResult Invoke<[DynamicallyAccessedMembers(JsonSerialized)] TResult>(string identifier, params object?[]? args)
    {
        args ??= [];
        return TryAnswer<TResult>(Mocks, Mode, Mocks.Strict, identifier, null, args, Log, out var mocked) ? mocked : inner.Invoke<TResult>(identifier, args);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public IJSInProcessObjectReference InvokeConstructor(string identifier, object?[]? args)
    {
        Log.Add("new " + identifier, null, args, mocked: false);
        return inner.InvokeConstructor(identifier, args);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public TValue GetValue<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier)
    {
        Log.Add("get " + identifier, null, null, mocked: false);
        return inner.GetValue<TValue>(identifier);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public void SetValue<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, TValue value)
    {
        Log.Add("set " + identifier, null, [value], mocked: false);
        inner.SetValue(identifier, value);
    }
}

/// <summary>A module imported by a story component: records calls and answers the module's mocks.</summary>
internal class StoryJSObjectReference(IJSObjectReference? real, string module, JSInteropMocks? mocks, JSMockMode mode, bool strict, JSCallLog log) : IJSObjectReference
{
    protected JSMockMode Mode => mode;

    protected IJSObjectReference? Real => real;

    protected string Module => module;

    protected JSInteropMocks? Mocks => mocks;

    protected bool Strict => strict;

    protected JSCallLog Log => log;

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, object?[]? args) =>
        InvokeCoreAsync<TValue>(identifier, args, CancellationToken.None, hasToken: false);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeCoreAsync<TValue>(identifier, args, cancellationToken, hasToken: true);

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args)
    {
        log.Add("new " + identifier, module, args, mocked: false);
        return RealOrThrow(identifier).InvokeConstructorAsync(identifier, args);
    }

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        log.Add("new " + identifier, module, args, mocked: false);
        return RealOrThrow(identifier).InvokeConstructorAsync(identifier, cancellationToken, args);
    }

    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier)
    {
        log.Add("get " + identifier, module, null, mocked: false);
        return RealOrThrow(identifier).GetValueAsync<TValue>(identifier);
    }

    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken)
    {
        log.Add("get " + identifier, module, null, mocked: false);
        return RealOrThrow(identifier).GetValueAsync<TValue>(identifier, cancellationToken);
    }

    public ValueTask SetValueAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, TValue value)
    {
        log.Add("set " + identifier, module, [value], mocked: false);
        return RealOrThrow(identifier).SetValueAsync(identifier, value);
    }

    public ValueTask SetValueAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, TValue value, CancellationToken cancellationToken)
    {
        log.Add("set " + identifier, module, [value], mocked: false);
        return RealOrThrow(identifier).SetValueAsync(identifier, value, cancellationToken);
    }

    public ValueTask DisposeAsync() => real?.DisposeAsync() ?? ValueTask.CompletedTask;

    protected IJSObjectReference RealOrThrow(string identifier) =>
        real ?? throw new InvalidOperationException($"The mocked module '{module}' could not be loaded, so '{identifier}' needs a setup in the story's MockJS(...).");

    private async ValueTask<TValue> InvokeCoreAsync<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, object?[]? args, CancellationToken cancellationToken, bool hasToken)
    {
        args ??= [];
        if (StoryJSRuntime.TryAnswer<TValue>(mocks, mode, strict, identifier, module, args, log, out var mocked))
        {
            return mocked;
        }

        if (real is null)
        {
            // Mocked module that does not exist: unmocked calls behave like bUnit's loose mode.
            return strict ? throw new InvalidOperationException($"The JS call '{module}:{identifier}' has no setup.") : default!;
        }

        return hasToken
            ? await real.InvokeAsync<TValue>(identifier, cancellationToken, args).ConfigureAwait(false)
            : await real.InvokeAsync<TValue>(identifier, args).ConfigureAwait(false);
    }
}

/// <summary><see cref="StoryJSObjectReference"/> over an in-process (WebAssembly) module.</summary>
internal sealed class InProcessStoryJSObjectReference(IJSInProcessObjectReference real, string module, JSInteropMocks? mocks, JSMockMode mode, bool strict, JSCallLog log)
    : StoryJSObjectReference(real, module, mocks, mode, strict, log), IJSInProcessObjectReference
{
    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public TValue Invoke<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, params object?[]? args)
    {
        args ??= [];
        return StoryJSRuntime.TryAnswer<TValue>(Mocks, Mode, Strict, identifier, Module, args, Log, out var mocked) ? mocked : real.Invoke<TValue>(identifier, args);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public IJSInProcessObjectReference InvokeConstructor(string identifier, object?[]? args)
    {
        Log.Add("new " + identifier, Module, args, mocked: false);
        return real.InvokeConstructor(identifier, args);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public TValue GetValue<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier)
    {
        Log.Add("get " + identifier, Module, null, mocked: false);
        return real.GetValue<TValue>(identifier);
    }

    [RequiresUnreferencedCode(StoryJSRuntime.InProcessMessage)]
    public void SetValue<[DynamicallyAccessedMembers(StoryJSRuntime.JsonSerialized)] TValue>(string identifier, TValue value)
    {
        Log.Add("set " + identifier, Module, [value], mocked: false);
        real.SetValue(identifier, value);
    }

    public void Dispose() => real.Dispose();
}
