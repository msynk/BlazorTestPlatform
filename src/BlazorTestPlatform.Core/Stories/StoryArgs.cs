using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>The input a workbench control uses for a parameter.</summary>
public enum ArgControlType
{
    /// <summary>Free text (strings, characters, GUIDs, URIs, flags enums, durations, offsets).</summary>
    Text,

    /// <summary>A checkbox.</summary>
    Boolean,

    /// <summary>A number input (integers step 1, floating point any step).</summary>
    Number,

    /// <summary>A drop-down of named values (enums, nullable booleans).</summary>
    Select,

    /// <summary>A date input (<c>DateOnly</c>).</summary>
    Date,

    /// <summary>A date and time input (<c>DateTime</c>).</summary>
    DateTime,

    /// <summary>A time input (<c>TimeOnly</c>).</summary>
    Time,
}

/// <summary>
/// A component parameter that the workbench can edit live: its type, the control to show and the value the story
/// renders with when nothing is overridden (the story's argument, else the component's own default).
/// </summary>
public sealed record ArgControl
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Declared parameter type.</summary>
    public required Type ParameterType { get; init; }

    /// <summary>Input to render.</summary>
    public required ArgControlType Control { get; init; }

    /// <summary>Choices of a <see cref="ArgControlType.Select"/> control (an empty first entry means "unset").</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>True when the parameter accepts null (reference and nullable value types).</summary>
    public bool Nullable { get; init; }

    /// <summary>True when the value comes from a factory (computed per render), so there is no fixed default to show.</summary>
    public bool Computed { get; init; }

    /// <summary>True when the story sets the parameter (otherwise the component's default applies).</summary>
    public bool SetByStory { get; init; }

    /// <summary>The default as control text (see <see cref="StoryArgs.Format"/>); null for null or unknown values.</summary>
    public string? DefaultText { get; init; }

    /// <summary>Short type name for hints, e.g. <c>int?</c>.</summary>
    public string TypeName => StoryArgs.TypeName(ParameterType);
}

/// <summary>The outcome of resolving textual arg overrides against a story.</summary>
/// <param name="Values">Converted values by parameter name, ready for <see cref="StoryRenderContext.ArgOverrides"/>.</param>
/// <param name="Errors">Messages for overrides that were rejected (unknown parameter, invalid value...).</param>
public sealed record ResolvedArgs(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<string> Errors)
{
    /// <summary>No overrides.</summary>
    public static ResolvedArgs Empty { get; } = new(new Dictionary<string, object?>(), []);
}

/// <summary>
/// Live arg overrides: which parameters of a story can be edited, how values are written as text (the workbench URL
/// carries them as <c>arg.Name=value</c>) and how text converts back to parameter values. The same rules apply in
/// the workbench, in browser runs (<c>StoryPageOptions.Args</c>) and in bUnit (<c>BunitStoryOptions.Args</c>).
/// </summary>
public static class StoryArgs
{
    /// <summary>Query-string prefix of arg overrides: <c>?arg.Title=Hello</c>.</summary>
    public const string QueryPrefix = "arg.";

    /// <summary>Text that stands for null.</summary>
    public const string NullToken = "!null";

    /// <summary>The editable parameters of a story's component, in declaration order of the story then the component.</summary>
    public static IReadOnlyList<ArgControl> ControlsFor(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var parameters = ComponentParameters.Of(story.ComponentType);
        var defaults = ComponentDefaults(story.ComponentType);
        var byName = story.Args.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var controls = new List<ArgControl>();
        var ordered = story.Args.Select(a => a.Name)
            .Concat(parameters.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ordered)
        {
            if (!parameters.TryGetValue(name, out var property) || !IsEditable(property))
            {
                continue;
            }

            byName.TryGetValue(name, out var arg);
            if (arg is not null && arg.Kind is not (StoryArgKind.Value or StoryArgKind.Binding))
            {
                continue;
            }

            var type = property.PropertyType;
            if (ControlOf(type) is not { } control)
            {
                continue;
            }

            var computed = arg is not null && !arg.HasConstant;
            object? defaultValue = arg is not null ? arg.ConstantValue : defaults.GetValueOrDefault(property.Name);
            controls.Add(new ArgControl
            {
                Name = property.Name,
                ParameterType = type,
                Control = control,
                Options = OptionsOf(type),
                Nullable = !type.IsValueType || System.Nullable.GetUnderlyingType(type) is not null,
                Computed = computed,
                SetByStory = arg is not null,
                DefaultText = computed ? null : Format(defaultValue),
            });
        }

        return controls;
    }

    /// <summary>
    /// Converts textual overrides into parameter values. Overrides equal to the story's own value are dropped, so
    /// links stay short. With <paramref name="fromLink"/>, strings containing <c>&lt;</c> are rejected: a shared link
    /// must never be able to inject markup into a component that renders a parameter as HTML.
    /// </summary>
    public static ResolvedArgs Resolve(Story story, IReadOnlyDictionary<string, string?> overrides, bool fromLink = true)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(overrides);
        if (overrides.Count == 0)
        {
            return ResolvedArgs.Empty;
        }

        var controls = ControlsFor(story).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var (name, text) in overrides)
        {
            if (!controls.TryGetValue(name, out var control))
            {
                errors.Add($"'{name}' is not an editable parameter of {story.ComponentType.Name}.");
                continue;
            }

            // An empty field for a parameter without a default (unset or computed) means "leave it alone": that is
            // what a form submitted without JavaScript sends for every untouched text input.
            if (text is { Length: 0 } && control.DefaultText is null)
            {
                continue;
            }

            if (fromLink && text is not null && text.Contains('<', StringComparison.Ordinal))
            {
                errors.Add($"'{control.Name}': values from links cannot contain '<'. Type the value in the control instead.");
                continue;
            }

            if (!TryConvert(text, control.ParameterType, out var value, out var error))
            {
                errors.Add($"'{control.Name}': {error}");
                continue;
            }

            if (!control.Computed && Format(value) == control.DefaultText)
            {
                continue;
            }

            values[control.Name] = value;
        }

        return new ResolvedArgs(values, errors);
    }

    /// <summary>Writes a value as control text: invariant culture, ISO dates, enum names, <c>true</c>/<c>false</c>.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        Enum enumValue => enumValue.ToString(),
        DateTime dateTime => dateTime.ToString(dateTime.TimeOfDay.Ticks % TimeSpan.TicksPerMinute == 0 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString(time.Second == 0 && time.Millisecond == 0 ? "HH:mm" : "HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Converts control text to <paramref name="type"/>. <see cref="NullToken"/> (or empty text for nullable value types) is null.</summary>
    public static bool TryConvert(string? text, Type type, out object? value, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(type);
        value = null;
        error = null;
        var underlying = System.Nullable.GetUnderlyingType(type);
        var nullable = !type.IsValueType || underlying is not null;
        var target = underlying ?? type;

        if (text is null || text == NullToken || (underlying is not null && text.Length == 0))
        {
            if (nullable)
            {
                return true;
            }

            error = $"{TypeName(type)} cannot be null.";
            return false;
        }

        var invariant = CultureInfo.InvariantCulture;
        var trimmed = target == typeof(string) ? text : text.Trim();
        var ok = true;
        object? converted = null;
        if (target == typeof(string))
        {
            converted = text;
        }
        else if (target == typeof(bool))
        {
            ok = bool.TryParse(trimmed, out var b);
            converted = b;
        }
        else if (target.IsEnum)
        {
            ok = Enum.TryParse(target, trimmed, ignoreCase: true, out converted) && (target.IsDefined(typeof(FlagsAttribute), false) || Enum.IsDefined(target, converted!));
        }
        else if (target == typeof(char))
        {
            ok = text.Length == 1;
            converted = ok ? text[0] : null;
        }
        else if (target == typeof(int)) { ok = int.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(long)) { ok = long.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(short)) { ok = short.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(byte)) { ok = byte.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(sbyte)) { ok = sbyte.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(uint)) { ok = uint.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(ulong)) { ok = ulong.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(ushort)) { ok = ushort.TryParse(trimmed, NumberStyles.Integer, invariant, out var v); converted = v; }
        else if (target == typeof(double)) { ok = double.TryParse(trimmed, NumberStyles.Float, invariant, out var v); converted = v; }
        else if (target == typeof(float)) { ok = float.TryParse(trimmed, NumberStyles.Float, invariant, out var v); converted = v; }
        else if (target == typeof(decimal)) { ok = decimal.TryParse(trimmed, NumberStyles.Number, invariant, out var v); converted = v; }
        else if (target == typeof(DateTime)) { ok = DateTime.TryParse(trimmed, invariant, DateTimeStyles.RoundtripKind, out var v); converted = v; }
        else if (target == typeof(DateTimeOffset)) { ok = DateTimeOffset.TryParse(trimmed, invariant, DateTimeStyles.AssumeUniversal, out var v); converted = v; }
        else if (target == typeof(DateOnly)) { ok = DateOnly.TryParse(trimmed, invariant, out var v); converted = v; }
        else if (target == typeof(TimeOnly)) { ok = TimeOnly.TryParse(trimmed, invariant, out var v); converted = v; }
        else if (target == typeof(TimeSpan)) { ok = TimeSpan.TryParse(trimmed, invariant, out var v); converted = v; }
        else if (target == typeof(Guid)) { ok = Guid.TryParse(trimmed, out var v); converted = v; }
        else if (target == typeof(Uri)) { ok = Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var v); converted = v; }
        else
        {
            error = $"{TypeName(type)} parameters cannot be edited.";
            return false;
        }

        if (!ok)
        {
            error = $"'{text}' is not a valid {TypeName(type)}" + (target.IsEnum ? $" ({string.Join(", ", Enum.GetNames(target))})." : ".");
            return false;
        }

        value = converted;
        return true;
    }

    /// <summary>Encodes overrides as <c>Name=value&amp;...</c> (the frame-bridge token, also the query without the prefix).</summary>
    public static string Encode(IEnumerable<KeyValuePair<string, string?>> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var builder = new StringBuilder();
        foreach (var (name, value) in overrides)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value ?? NullToken));
        }

        return builder.ToString();
    }

    /// <summary>Decodes <see cref="Encode"/> output (also accepts <c>+</c> for spaces, as browsers write it).</summary>
    public static IReadOnlyDictionary<string, string?> Decode(string? token) => Parse(token, prefix: null);

    /// <summary>Reads <c>arg.Name=value</c> pairs from a query string (with or without the leading <c>?</c>). The last value of a name wins.</summary>
    public static IReadOnlyDictionary<string, string?> FromQuery(string? query) => Parse(query, QueryPrefix);

    /// <summary>The query-string fragment for overrides: <c>arg.Name=value&amp;...</c>.</summary>
    public static string ToQuery(IEnumerable<KeyValuePair<string, string?>> overrides) =>
        Encode((overrides ?? throw new ArgumentNullException(nameof(overrides))).Select(p => new KeyValuePair<string, string?>(QueryPrefix + p.Key, p.Value)));

    internal static string TypeName(Type type)
    {
        if (System.Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeName(underlying) + "?";
        }

        return type switch
        {
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(int) => "int",
            _ when type == typeof(long) => "long",
            _ when type == typeof(double) => "double",
            _ when type == typeof(float) => "float",
            _ when type == typeof(decimal) => "decimal",
            _ when type == typeof(char) => "char",
            _ => type.Name,
        };
    }

    private static Dictionary<string, string?> Parse(string? text, string? prefix)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (var pair in text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var rawName = Unescape(equals < 0 ? pair : pair[..equals]);
            if (prefix is not null)
            {
                if (!rawName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rawName = rawName[prefix.Length..];
            }

            if (rawName.Length == 0)
            {
                continue;
            }

            var value = equals < 0 ? string.Empty : Unescape(pair[(equals + 1)..]);
            result[rawName] = value == NullToken ? null : value;
        }

        return result;
    }

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private static bool IsEditable(PropertyInfo property)
    {
        if (property.GetCustomAttribute<ParameterAttribute>(inherit: true) is not { CaptureUnmatchedValues: false })
        {
            return false;
        }

        return !property.IsDefined(typeof(CascadingParameterAttribute), inherit: true);
    }

    private static ArgControlType? ControlOf(Type type)
    {
        var target = System.Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum)
        {
            return target.IsDefined(typeof(FlagsAttribute), false) ? ArgControlType.Text : ArgControlType.Select;
        }

        if (target == typeof(bool))
        {
            return target == type ? ArgControlType.Boolean : ArgControlType.Select;
        }

        if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte) || target == typeof(sbyte)
            || target == typeof(uint) || target == typeof(ulong) || target == typeof(ushort) || target == typeof(double) || target == typeof(float)
            || target == typeof(decimal))
        {
            return ArgControlType.Number;
        }

        if (target == typeof(DateOnly))
        {
            return ArgControlType.Date;
        }

        if (target == typeof(DateTime))
        {
            return ArgControlType.DateTime;
        }

        if (target == typeof(TimeOnly))
        {
            return ArgControlType.Time;
        }

        if (target == typeof(string) || target == typeof(char) || target == typeof(Guid) || target == typeof(Uri)
            || target == typeof(TimeSpan) || target == typeof(DateTimeOffset))
        {
            return ArgControlType.Text;
        }

        return null;
    }

    private static IReadOnlyList<string> OptionsOf(Type type)
    {
        var underlying = System.Nullable.GetUnderlyingType(type);
        var target = underlying ?? type;
        if (target == typeof(bool) && underlying is not null)
        {
            return [string.Empty, "true", "false"];
        }

        if (target.IsEnum && !target.IsDefined(typeof(FlagsAttribute), false))
        {
            var names = Enum.GetNames(target);
            return underlying is null ? names : [string.Empty, .. names];
        }

        return [];
    }

    /// <summary>The component's own default for a parameter (null when unknown).</summary>
    internal static object? ComponentDefault(Story story, string name) => ComponentDefaults(story.ComponentType).GetValueOrDefault(name);

    // Parameter defaults come from a fresh instance: component constructors only initialize fields, and nothing is
    // rendered or injected. Components without a public parameterless constructor show no defaults.
    private static Dictionary<string, object?> ComponentDefaults([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        object? instance;
        try
        {
            instance = componentType.GetConstructor(Type.EmptyTypes) is null ? null : Activator.CreateInstance(componentType);
        }
        catch (TargetInvocationException)
        {
            instance = null;
        }

        if (instance is null)
        {
            return defaults;
        }

        foreach (var (name, property) in ComponentParameters.Of(componentType))
        {
            if (property.GetMethod is not null && ControlOf(property.PropertyType) is not null)
            {
                try
                {
                    defaults[name] = property.GetValue(instance);
                }
                catch (TargetInvocationException)
                {
                    // A getter that depends on rendering state: no default to show.
                }
            }
        }

        (instance as IDisposable)?.Dispose();
        return defaults;
    }
}
