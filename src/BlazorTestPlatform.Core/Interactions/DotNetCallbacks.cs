using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace BlazorTestPlatform;

/// <summary>
/// Calls <c>[JSInvokable]</c> methods of the <see cref="DotNetObjectReference{TValue}"/> a component passed to JavaScript,
/// as the JavaScript side would (a ResizeObserver firing, an editor reporting a change, a map reporting a click). Drivers
/// run the call on the story's renderer, so the component renders the result as it would in the app. Arguments travel
/// as JSON: strings, numbers, booleans, enums, GUIDs and dates always; other types where reflection-based JSON
/// serialization is enabled (tests, and apps that do not disable it).
/// </summary>
public static class DotNetCallbacks
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>True for a <see cref="DotNetObjectReference{TValue}"/>.</summary>
    public static bool IsReference(object? value) =>
        value is not null && value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(DotNetObjectReference<>);

    /// <summary>Serializes call arguments (see the type remarks for what is supported).</summary>
    public static string ArgumentsToJson(IReadOnlyList<object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var argument in arguments)
            {
                WriteValue(writer, argument);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Invokes <paramref name="method"/> (its <c>[JSInvokable]</c> identifier, or its name) on the object
    /// <paramref name="reference"/> wraps, with <paramref name="argumentsJson"/> (a JSON array), awaits it, and returns the
    /// result as JSON (<c>null</c> for void and tasks without a result).
    /// </summary>
    /// <exception cref="ArgumentException">The reference is not a <see cref="DotNetObjectReference{TValue}"/>, no invokable method has that name, or the arguments do not fit.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "DotNetObjectReference<TValue> preserves TValue's public methods (DynamicallyAccessedMembers.PublicMethods) for JS interop, which is how Blazor itself calls them.")]
    public static async Task<string?> InvokeAsync(object reference, string method, string argumentsJson)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(argumentsJson);
        if (!IsReference(reference))
        {
            throw new ArgumentException($"{reference.GetType().Name} is not a DotNetObjectReference.", nameof(reference));
        }

        var target = reference.GetType().GetProperty(nameof(DotNetObjectReference<object>.Value))!.GetValue(reference)
            ?? throw new InvalidOperationException("The DotNetObjectReference was disposed.");
        var candidates = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<JSInvokableAttribute>() is { } invokable && string.Equals(invokable.Identifier ?? m.Name, method, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length != 1)
        {
            var available = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.GetCustomAttribute<JSInvokableAttribute>() is { } a ? a.Identifier ?? m.Name : null)
                .OfType<string>()
                .Order(StringComparer.Ordinal);
            throw new ArgumentException(
                candidates.Length == 0
                    ? $"{target.GetType().Name} has no [JSInvokable] method '{method}'. Invokable: {string.Join(", ", available)}."
                    : $"{target.GetType().Name} has {candidates.Length} [JSInvokable] methods named '{method}'.",
                nameof(method));
        }

        var invoked = candidates[0];
        using var document = JsonDocument.Parse(argumentsJson);
        var given = document.RootElement.EnumerateArray().ToArray();
        var parameters = invoked.GetParameters();
        if (given.Length > parameters.Length || parameters.Skip(given.Length).Any(p => !p.HasDefaultValue))
        {
            throw new ArgumentException($"{target.GetType().Name}.{invoked.Name} takes {parameters.Length} argument(s); {given.Length} given.", nameof(argumentsJson));
        }

        var values = parameters.Select((p, i) => i < given.Length ? ReadValue(given[i], p.ParameterType) : p.DefaultValue).ToArray();
        object? result;
        try
        {
            result = invoked.Invoke(target, values);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var returns = invoked.ReturnType;
        if (returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            result = returns.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(result, null);
            returns = typeof(Task<>).MakeGenericType(returns.GetGenericArguments()[0]);
        }

        switch (result)
        {
            case Task task:
                await task.ConfigureAwait(false);
                result = returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(Task<>) ? returns.GetProperty(nameof(Task<object>.Result))!.GetValue(task) : null;
                break;
            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                result = null;
                break;
        }

        return result is null ? null : ArgumentsToJson([result])[1..^1];
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Complex values are serialized only when reflection-based serialization is enabled (checked).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Complex values are serialized only when reflection-based serialization is enabled (checked).")]
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case Enum enumValue:
                writer.WriteStringValue(enumValue.ToString());
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong unsigned:
                writer.WriteNumberValue(unsigned);
                break;
            case float or double:
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            default:
                if (!JsonSerializer.IsReflectionEnabledByDefault)
                {
                    throw new NotSupportedException($"Cannot pass a {value.GetType().Name} to a [JSInvokable] method here: reflection-based JSON serialization is disabled. Pass primitives.");
                }

                JsonSerializer.Serialize(writer, value, value.GetType(), Web);
                break;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Complex values are read only when reflection-based serialization is enabled (checked).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Complex values are read only when reflection-based serialization is enabled (checked).")]
    private static object? ReadValue(JsonElement element, Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return type.IsValueType && nullable is null ? throw new ArgumentException($"null is not a {type.Name}.") : null;
        }

        var target = nullable ?? type;
        if (target == typeof(string))
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        }

        if (target == typeof(bool))
        {
            return element.GetBoolean();
        }

        if (target.IsEnum)
        {
            return element.ValueKind == JsonValueKind.String ? Enum.Parse(target, element.GetString()!, ignoreCase: true) : Enum.ToObject(target, element.GetInt64());
        }

        if (target == typeof(Guid))
        {
            return element.GetGuid();
        }

        if (target == typeof(DateTime))
        {
            return element.GetDateTime();
        }

        if (target == typeof(DateTimeOffset))
        {
            return element.GetDateTimeOffset();
        }

        if (target == typeof(decimal))
        {
            return element.GetDecimal();
        }

        if (target == typeof(double) || target == typeof(float))
        {
            return Convert.ChangeType(element.GetDouble(), target, CultureInfo.InvariantCulture);
        }

        if (target.IsPrimitive && target != typeof(char) && target != typeof(IntPtr) && target != typeof(UIntPtr))
        {
            return target == typeof(ulong) ? element.GetUInt64() : Convert.ChangeType(element.GetInt64(), target, CultureInfo.InvariantCulture);
        }

        if (target == typeof(JsonElement))
        {
            return element.Clone();
        }

        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            throw new NotSupportedException($"Cannot pass a {target.Name} to a [JSInvokable] method here: reflection-based JSON serialization is disabled. Pass primitives.");
        }

        return element.Deserialize(type, Web);
    }
}
