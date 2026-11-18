using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>What kind of component parameter a <see cref="ComponentParameterDoc"/> describes.</summary>
public enum ComponentParameterKind
{
    /// <summary>A value (<c>[Parameter]</c>).</summary>
    Value,

    /// <summary>Content the parent renders into the component (<c>RenderFragment</c>, <c>RenderFragment&lt;T&gt;</c>).</summary>
    Content,

    /// <summary>A callback the component raises (<c>EventCallback</c>, <c>EventCallback&lt;T&gt;</c>).</summary>
    Event,

    /// <summary>Attributes the component spreads onto an element (<c>CaptureUnmatchedValues</c>).</summary>
    Attributes,

    /// <summary>A value received from an ancestor (<c>[CascadingParameter]</c>).</summary>
    Cascading,
}

/// <summary>One parameter of a component, as its documentation page shows it.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Type">C#-style type name (<c>string?</c>, <c>EventCallback&lt;int&gt;</c>).</param>
/// <param name="Kind">What kind of parameter it is.</param>
/// <param name="Required">Marked <c>[EditorRequired]</c>.</param>
/// <param name="Default">The component's default as control text, when it is an editable parameter.</param>
/// <param name="Summary">The parameter's XML documentation summary, when the assembly ships its documentation file.</param>
/// <param name="Options">The values of an enum or bool parameter.</param>
public sealed record ComponentParameterDoc(string Name, string Type, ComponentParameterKind Kind, bool Required, string? Default, string? Summary, IReadOnlyList<string> Options);

/// <summary>The documentation page of a component (Storybook's autodocs): its summary, parameters and stories.</summary>
/// <param name="Title">The stories' title (<c>Inputs/Button</c>).</param>
/// <param name="Component">The component type's full name.</param>
/// <param name="Summary">The component's XML documentation summary, or null.</param>
/// <param name="Parameters">Every parameter, required ones first.</param>
/// <param name="Stories">The component's stories, in catalog order.</param>
public sealed record ComponentDoc(string Title, string Component, string? Summary, IReadOnlyList<ComponentParameterDoc> Parameters, IReadOnlyList<Story> Stories);

/// <summary>
/// Builds component documentation pages from stories (Storybook's autodocs): the parameter table comes from the component
/// type, defaults from the same controls the workbench edits, and summaries from the assembly's XML documentation file
/// (<c>&lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;</c>) when it is deployed next to it.
/// </summary>
public static partial class ComponentDocs
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> XmlCache = new();

    /// <summary>The documentation page of a story group.</summary>
    public static ComponentDoc For(StoryGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Stories.Count == 0)
        {
            return new ComponentDoc(group.Title, group.ComponentType.FullName ?? group.ComponentType.Name, null, [], []);
        }

        var first = group.Stories[0];
        var type = first.ComponentType;
        var xml = XmlDocsOf(type.Assembly);
        var controls = StoryArgs.ControlsFor(first).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var parameters = ComponentParameters.Of(type).Values
            .Select(p => Describe(p, controls.GetValueOrDefault(p.Name), xml))
            .OrderByDescending(p => p.Required)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        return new ComponentDoc(group.Title, type.FullName ?? type.Name, xml.GetValueOrDefault("T:" + XmlName(type)), parameters, group.Stories);
    }

    private static ComponentParameterDoc Describe(PropertyInfo property, ArgControl? control, IReadOnlyDictionary<string, string> xml)
    {
        var type = property.PropertyType;
        var parameter = property.GetCustomAttribute<ParameterAttribute>(inherit: true);
        var kind = property.IsDefined(typeof(CascadingParameterAttribute), inherit: true) ? ComponentParameterKind.Cascading
            : parameter?.CaptureUnmatchedValues == true ? ComponentParameterKind.Attributes
            : ComponentParameters.IsEventCallback(type) ? ComponentParameterKind.Event
            : type == typeof(RenderFragment) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RenderFragment<>)) ? ComponentParameterKind.Content
            : ComponentParameterKind.Value;
        return new ComponentParameterDoc(
            property.Name,
            FriendlyName(type, Nullable(property)),
            kind,
            property.IsDefined(typeof(EditorRequiredAttribute), inherit: true),
            control?.DefaultText,
            xml.GetValueOrDefault("P:" + XmlName(property.DeclaringType!) + "." + property.Name),
            control?.Options.Where(o => o.Length > 0).ToArray() ?? []);
    }

    private static bool Nullable(PropertyInfo property) =>
        !property.PropertyType.IsValueType && new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;

    /// <summary>A C#-style name: <c>int?</c>, <c>string</c>, <c>EventCallback&lt;MouseEventArgs&gt;</c>, <c>IReadOnlyList&lt;Person&gt;</c>.</summary>
    public static string FriendlyName(Type type, bool nullableReference = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (System.Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return FriendlyName(underlying) + "?";
        }

        var name = type switch
        {
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(int) => "int",
            _ when type == typeof(long) => "long",
            _ when type == typeof(double) => "double",
            _ when type == typeof(decimal) => "decimal",
            _ when type == typeof(float) => "float",
            _ when type == typeof(object) => "object",
            _ when type.IsArray => FriendlyName(type.GetElementType()!) + "[]",
            _ when type.IsGenericType => type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(a => FriendlyName(a))) + ">",
            _ => type.Name,
        };
        return nullableReference ? name + "?" : name;
    }

    // XML documentation ids: nested types use '.', generic types keep their arity (`1).
    private static string XmlName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is empty in single-file apps; the documentation is then reported as missing.")]
    private static IReadOnlyDictionary<string, string> XmlDocsOf(Assembly assembly) => XmlCache.GetOrAdd(assembly, static a =>
    {
        try
        {
            var path = string.IsNullOrEmpty(a.Location) ? null : Path.ChangeExtension(a.Location, ".xml");
            if (path is null || !File.Exists(path))
            {
                return new Dictionary<string, string>();
            }

            return XDocument.Load(path).Descendants("member")
                .Select(m => (Name: (string?)m.Attribute("name"), Summary: m.Element("summary")))
                .Where(m => m.Name is not null && m.Summary is not null)
                .GroupBy(m => m.Name!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Text(g.First().Summary!), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or NotSupportedException)
        {
            return new Dictionary<string, string>();
        }
    });

    // <see cref="T:A.B"/> -> B, <paramref name="x"/> and <c>x</c> -> x, whitespace collapsed.
    internal static string Text(XElement summary)
    {
        foreach (var reference in summary.Descendants().Where(e => e.Name.LocalName is "see" or "seealso" or "paramref" or "typeparamref").ToArray())
        {
            var target = (string?)reference.Attribute("cref") ?? (string?)reference.Attribute("name") ?? (string?)reference.Attribute("langword") ?? reference.Value;
            var shortName = target.Contains(':', StringComparison.Ordinal) ? target[(target.IndexOf(':', StringComparison.Ordinal) + 1)..] : target;
            shortName = shortName.Split('(')[0];
            reference.ReplaceWith(string.IsNullOrEmpty(reference.Value) ? shortName.Split('.')[^1].Split('`')[0] : reference.Value);
        }

        // Text by value (XText.ToString would escape & < > again, and the page encodes them once).
        return Whitespace().Replace(string.Concat(summary.Nodes().Select(n => n switch { XElement e => e.Value, XText t => t.Value, _ => string.Empty })), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
