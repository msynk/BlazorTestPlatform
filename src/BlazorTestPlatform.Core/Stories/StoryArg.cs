using System.Globalization;

namespace BlazorTestPlatform;

/// <summary>How a story argument is produced.</summary>
public enum StoryArgKind
{
    /// <summary>A plain parameter value.</summary>
    Value,

    /// <summary>An <c>EventCallback</c> that records invocations into the <see cref="ActionLog"/>.</summary>
    Action,

    /// <summary>A two-way bound value (<c>Value</c>/<c>ValueChanged</c>) held by the story.</summary>
    Binding,

    /// <summary>A render fragment (child content or template).</summary>
    Fragment,
}

/// <summary>One component parameter supplied by a story.</summary>
public sealed class StoryArg
{
    private readonly Func<StoryRenderContext, object?> _factory;

    internal StoryArg(string name, StoryArgKind kind, Type? valueType, Func<StoryRenderContext, object?> factory, string? display)
    {
        Name = name;
        Kind = kind;
        ValueType = valueType;
        _factory = factory;
        Display = display;
    }

    internal StoryArg(string name, StoryArgKind kind, Type? valueType, object? constant, Func<StoryRenderContext, object?> factory, string? display)
        : this(name, kind, valueType, factory, display)
    {
        HasConstant = true;
        ConstantValue = constant;
    }

    /// <summary>Component parameter name.</summary>
    public string Name { get; }

    /// <summary>How the value is produced.</summary>
    public StoryArgKind Kind { get; }

    /// <summary>Declared parameter type, when known.</summary>
    public Type? ValueType { get; }

    /// <summary>Human-readable value for the workbench and reports.</summary>
    public string? Display { get; }

    /// <summary>True when the story sets a fixed value (rather than one computed per render).</summary>
    public bool HasConstant { get; }

    /// <summary>The fixed value (the initial value for bindings) when <see cref="HasConstant"/>.</summary>
    public object? ConstantValue { get; }

    /// <summary>Produces the parameter value for a render.</summary>
    public object? CreateValue(StoryRenderContext context) => _factory(context);

    internal static string? Describe(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        Delegate => "(delegate)",
        _ => value.ToString(),
    };

    /// <inheritdoc />
    public override string ToString() => $"{Name} = {Display}";
}

/// <summary>
/// Wraps a story's render output, e.g. in a layout container, cascading value or theme provider.
/// Decorators registered first wrap closest to the component.
/// </summary>
/// <param name="story">The fragment being wrapped.</param>
/// <param name="context">The render context.</param>
public delegate Microsoft.AspNetCore.Components.RenderFragment StoryDecorator(Microsoft.AspNetCore.Components.RenderFragment story, StoryRenderContext context);
