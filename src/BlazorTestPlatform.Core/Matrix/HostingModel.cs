namespace BlazorTestPlatform;

/// <summary>
/// Where and how a story is rendered. One story definition can be executed under every hosting model,
/// which is what makes cross-hosting regressions (Server works, WebAssembly breaks) visible.
/// </summary>
public enum HostingModel
{
    /// <summary>In-memory rendering through bUnit. No browser, no layout, fastest feedback.</summary>
    InMemory,

    /// <summary>Static server-side rendering (no interactivity).</summary>
    StaticServer,

    /// <summary>Interactive Server render mode (SignalR circuit).</summary>
    InteractiveServer,

    /// <summary>Interactive WebAssembly render mode.</summary>
    InteractiveWebAssembly,

    /// <summary>Interactive Auto render mode (Server first, WebAssembly once downloaded).</summary>
    InteractiveAuto,
}

/// <summary>Helpers for <see cref="HostingModel"/>.</summary>
public static class HostingModelExtensions
{
    /// <summary>True when event handlers run (everything except static SSR).</summary>
    public static bool IsInteractive(this HostingModel model) => model is not HostingModel.StaticServer;

    /// <summary>True when the story is rendered in a real browser.</summary>
    public static bool IsBrowser(this HostingModel model) => model is not HostingModel.InMemory;

    /// <summary>Short token used in URLs, snapshot names and case keys.</summary>
    public static string ToToken(this HostingModel model) => model switch
    {
        HostingModel.InMemory => "memory",
        HostingModel.StaticServer => "ssr",
        HostingModel.InteractiveServer => "server",
        HostingModel.InteractiveWebAssembly => "wasm",
        HostingModel.InteractiveAuto => "auto",
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, null),
    };

    /// <summary>Parses a token produced by <see cref="ToToken"/> (case-insensitive). Also accepts enum names.</summary>
    public static bool TryParse(string? token, out HostingModel model)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "memory" or "inmemory" or "bunit":
                model = HostingModel.InMemory;
                return true;
            case "ssr" or "static" or "staticserver":
                model = HostingModel.StaticServer;
                return true;
            case "server" or "interactiveserver":
                model = HostingModel.InteractiveServer;
                return true;
            case "wasm" or "webassembly" or "interactivewebassembly":
                model = HostingModel.InteractiveWebAssembly;
                return true;
            case "auto" or "interactiveauto":
                model = HostingModel.InteractiveAuto;
                return true;
            default:
                model = default;
                return false;
        }
    }

    /// <summary>Parses a hosting-model token or throws.</summary>
    public static HostingModel Parse(string token) =>
        TryParse(token, out var model) ? model : throw new FormatException($"Unknown hosting model '{token}'. Expected one of: memory, ssr, server, wasm, auto.");
}
