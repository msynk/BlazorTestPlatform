using Bunit;

namespace BlazorTestPlatform.Bunit;

/// <summary>Registers a story's <see cref="JSInteropMocks"/> as bUnit JS-interop setups.</summary>
internal sealed class JSInteropMockAdapter(BunitJSInterop jsInterop) : IJSInteropMockVisitor
{
    public static void Apply(BunitJSInterop jsInterop, JSInteropMocks mocks)
    {
        if (mocks.Strict)
        {
            jsInterop.Mode = JSRuntimeMode.Strict;
        }

        var adapter = new JSInteropMockAdapter(jsInterop);
        foreach (var mock in mocks.Mocks)
        {
            mock.Accept(adapter);
        }

        foreach (var (module, moduleMocks) in mocks.Modules)
        {
            var moduleInterop = jsInterop.SetupModule(module);
            if (mocks.Strict || moduleMocks.Strict)
            {
                moduleInterop.Mode = JSRuntimeMode.Strict;
            }

            Apply(moduleInterop, moduleMocks);
        }
    }

    public void Visit<TResult>(string identifier, TResult result, IReadOnlyList<object?>? arguments)
    {
        var handler = arguments is null
            ? jsInterop.Setup<TResult>(identifier, _ => true)
            : jsInterop.Setup<TResult>(identifier, [.. arguments]);
        handler.SetResult(result);
    }

    public void VisitVoid(string identifier, IReadOnlyList<object?>? arguments)
    {
        var handler = arguments is null
            ? jsInterop.SetupVoid(identifier, _ => true)
            : jsInterop.SetupVoid(identifier, [.. arguments]);
        handler.SetVoidResult();
    }
}
