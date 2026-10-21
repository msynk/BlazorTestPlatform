using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorTestPlatform.Drivers;

/// <summary>
/// Reflection-free JSON for drivers that talk to JavaScript (locator chains, actions, element snapshots).
/// Safe in trimmed WebAssembly apps.
/// </summary>
public static class DriverJson
{
    /// <summary>Serializes a locator chain for the in-browser locator engines.</summary>
    public static string Steps(LocatorChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return Write(writer => WriteSteps(writer, chain));
    }

    /// <summary>Serializes an element action.</summary>
    public static string Action(ElementAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("kind", action.Kind switch
            {
                ElementActionKind.Click => "click",
                ElementActionKind.DoubleClick => "dblclick",
                ElementActionKind.Hover => "hover",
                ElementActionKind.Focus => "focus",
                ElementActionKind.Blur => "blur",
                ElementActionKind.Fill => "fill",
                ElementActionKind.Clear => "clear",
                ElementActionKind.Type => "type",
                ElementActionKind.Press => "press",
                ElementActionKind.Check => "check",
                ElementActionKind.Uncheck => "uncheck",
                ElementActionKind.SelectOption => "select",
                ElementActionKind.PointerDown => "pointerdown",
                ElementActionKind.PointerUp => "pointerup",
                ElementActionKind.DragTo => "drag",
                ElementActionKind.ScrollIntoView => "scroll",
                _ => throw new NotSupportedException(action.Kind.ToString()),
            });
            if (action.Text is not null)
            {
                writer.WriteString("text", action.Text);
            }

            writer.WriteStartArray("values");
            foreach (var value in action.Values ?? [])
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
            writer.WriteNumber("modifiers", (int)action.Modifiers);
            writer.WriteNumber("button", action.Button switch { MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 });
            writer.WriteBoolean("force", action.Force);
            if (action.Target is not null)
            {
                writer.WritePropertyName("target");
                WriteSteps(writer, action.Target);
            }

            writer.WriteEndObject();
        });
    }

    /// <summary>Parses element snapshots produced by in-browser describe functions.</summary>
    public static IReadOnlyList<ElementInfo> Elements(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize(json, DriverJsonContext.Default.ListElementInfo) ?? [];

    /// <summary>Parses one element snapshot (null for <c>null</c>).</summary>
    public static ElementInfo? Element(string? json) =>
        string.IsNullOrWhiteSpace(json) || json == "null" ? null : JsonSerializer.Deserialize(json, DriverJsonContext.Default.ElementInfo);

    private static void WriteSteps(Utf8JsonWriter writer, LocatorChain chain)
    {
        writer.WriteStartArray();
        foreach (var step in chain.Steps)
        {
            writer.WriteStartObject();
            switch (step.Kind)
            {
                case LocatorStepKind.Find:
                    writer.WriteString("kind", "find");
                    writer.WritePropertyName("query");
                    WriteQuery(writer, step.Query!);
                    break;
                case LocatorStepKind.Nth:
                    writer.WriteString("kind", "nth");
                    writer.WriteNumber("index", step.Index);
                    break;
                case LocatorStepKind.HasText:
                    writer.WriteString("kind", "hasText");
                    writer.WriteString("text", step.Text);
                    break;
                case LocatorStepKind.HasNotText:
                    writer.WriteString("kind", "hasNotText");
                    writer.WriteString("text", step.Text);
                    break;
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteQuery(Utf8JsonWriter writer, By query)
    {
        writer.WriteStartObject();
        switch (query)
        {
            case CssQuery css:
                writer.WriteString("type", "css");
                writer.WriteString("selector", css.Selector);
                break;
            case RoleQuery role:
                writer.WriteString("type", "role");
                writer.WriteString("role", role.RoleName);
                if (role.Name is null)
                {
                    writer.WriteNull("name");
                }
                else
                {
                    writer.WriteString("name", role.Name);
                }

                writer.WriteBoolean("exact", role.Exact);
                break;
            case TextQuery text:
                Text(writer, "text", text.Value, text.Exact);
                break;
            case LabelQuery label:
                Text(writer, "label", label.Value, label.Exact);
                break;
            case PlaceholderQuery placeholder:
                Text(writer, "placeholder", placeholder.Value, placeholder.Exact);
                break;
            case AltTextQuery alt:
                Text(writer, "alt", alt.Value, alt.Exact);
                break;
            case TitleQuery title:
                Text(writer, "title", title.Value, title.Exact);
                break;
            case TestIdQuery testId:
                writer.WriteString("type", "testid");
                writer.WriteString("value", testId.Value);
                writer.WriteString("attribute", By.TestIdAttribute);
                break;
            default:
                throw new NotSupportedException($"Query type '{query.GetType().Name}' is not supported.");
        }

        writer.WriteEndObject();
    }

    private static void Text(Utf8JsonWriter writer, string type, string value, bool exact)
    {
        writer.WriteString("type", type);
        writer.WriteString("value", value);
        writer.WriteBoolean("exact", exact);
    }

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<ElementInfo>))]
[JsonSerializable(typeof(ElementInfo))]
internal sealed partial class DriverJsonContext : JsonSerializerContext;
