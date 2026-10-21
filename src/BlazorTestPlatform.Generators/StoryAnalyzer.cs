using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorTestPlatform.Generators;

/// <summary>
/// Reports story mistakes at compile time that would otherwise surface when the catalog is built or the story renders:
/// arguments that are not component parameters, impossible bindings, misused unmatched attributes and duplicate stories.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StoryAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorTestPlatform.Stories";
    private const string StoryBuilderType = "BlazorTestPlatform.StoryBuilder<TComponent>";
    private const string VariantBuilderType = "BlazorTestPlatform.VariantBuilder<TComponent>";
    private const string CollectionBuilderType = "BlazorTestPlatform.StoryCollectionBuilder<TComponent>";
    private const string ParameterAttribute = "Microsoft.AspNetCore.Components.ParameterAttribute";
    private const string CascadingParameterAttribute = "Microsoft.AspNetCore.Components.CascadingParameterAttribute";
    private const string EventCallbackOfT = "Microsoft.AspNetCore.Components.EventCallback<TValue>";

    /// <summary>BTP001: the expression selects a property that is not a component parameter.</summary>
    public static readonly DiagnosticDescriptor NotAParameter = new(
        "BTP001",
        "Story argument is not a component parameter",
        "'{0}.{1}' is not a component parameter. Add [Parameter] to the property, or use Attribute(\"name\", value) for an unmatched attribute.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The story throws when the catalog is built, because only [Parameter] and [CascadingParameter] properties can be set.");

    /// <summary>BTP002: <c>Bind</c> on a parameter without a matching <c>{Name}Changed</c> callback.</summary>
    public static readonly DiagnosticDescriptor CannotBind = new(
        "BTP002",
        "Bound parameter has no matching Changed callback",
        "Cannot bind '{1}': '{0}' has no '{1}Changed' parameter of type EventCallback<{2}>. Use Arg(...) for a one-way value.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two-way binding needs a '{Name}Changed' EventCallback<T> parameter of the same type; the story throws when the catalog is built.");

    /// <summary>BTP003: <c>Attribute("Name", ...)</c> where Name is a declared parameter.</summary>
    public static readonly DiagnosticDescriptor AttributeIsParameter = new(
        "BTP003",
        "Use Arg for declared parameters",
        "'{1}' is a parameter of '{0}'. Use Arg(c => c.{1}, value) so the value is type-checked and editable in the workbench.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Attribute(...) passes an untyped value by name. For declared parameters, Arg(...) checks the type at compile time and enables live controls.");

    /// <summary>BTP004: <c>Attribute(...)</c> on a component that captures no unmatched values.</summary>
    public static readonly DiagnosticDescriptor NoUnmatchedValues = new(
        "BTP004",
        "Component does not accept unmatched attributes",
        "'{0}' has no [Parameter(CaptureUnmatchedValues = true)] property, so Attribute(\"{1}\", ...) fails when the story renders",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blazor throws when a component receives an attribute it neither declares nor captures.");

    /// <summary>BTP005: two stories with the same id in one collection.</summary>
    public static readonly DiagnosticDescriptor DuplicateStory = new(
        "BTP005",
        "Duplicate story",
        "Story '{0}' is declared twice in this collection: both get the id '{1}'. Rename one of them.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Story ids are slugs of the collection title and the story name; the catalog rejects duplicates.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(NotAParameter, CannotBind, AttributeIsParameter, NoUnmatchedValues, DuplicateStory);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName("BlazorTestPlatform.StoryBuilder`1") is null)
            {
                return;
            }

            start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            start.RegisterOperationBlockStartAction(block =>
            {
                var names = new List<(string Name, string Slug, Location Location)>();
                block.RegisterOperationAction(operation => CollectStoryName((IInvocationOperation)operation.Operation, names), OperationKind.Invocation);
                block.RegisterOperationBlockEndAction(end => ReportDuplicates(end, names));
            });
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var builder = method.ContainingType;
        if (builder is null || builder.TypeArguments.Length != 1 || builder.TypeArguments[0] is not INamedTypeSymbol component)
        {
            return;
        }

        var definition = builder.OriginalDefinition.ToDisplayString();
        if (definition == StoryBuilderType)
        {
            switch (method.Name)
            {
                case "Arg" or "Fragment" or "Template" or "Action":
                    CheckParameterExpression(context, invocation, component);
                    break;
                case "Bind":
                    if (CheckParameterExpression(context, invocation, component) is { } bound)
                    {
                        CheckBinding(context, invocation, component, bound);
                    }

                    break;
                case "Attribute":
                    CheckAttribute(context, invocation, component);
                    break;
            }
        }
        else if (definition == VariantBuilderType && method.Name == "Vary")
        {
            CheckParameterExpression(context, invocation, component);
        }
    }

    private static IPropertySymbol? CheckParameterExpression(OperationAnalysisContext context, IInvocationOperation invocation, INamedTypeSymbol component)
    {
        var argument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "parameter");
        if (argument is null || SelectedProperty(argument.Value) is not { } property)
        {
            return null;
        }

        if (!IsParameter(property))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotAParameter, argument.Syntax.GetLocation(), component.Name, property.Name));
            return null;
        }

        return property;
    }

    private static void CheckBinding(OperationAnalysisContext context, IInvocationOperation invocation, INamedTypeSymbol component, IPropertySymbol bound)
    {
        var valueType = invocation.TargetMethod.TypeArguments.FirstOrDefault() ?? bound.Type;
        var changed = Parameters(component).FirstOrDefault(p => string.Equals(p.Name, bound.Name + "Changed", System.StringComparison.OrdinalIgnoreCase));
        // Nullable reference annotations do not exist at run time (typeof(EventCallback<string?>) == typeof(EventCallback<string>)).
        var expected = changed?.Type is INamedTypeSymbol callback
            && callback.OriginalDefinition.ToDisplayString() == EventCallbackOfT
            && SymbolEqualityComparer.Default.Equals(callback.TypeArguments[0], valueType);
        if (!expected)
        {
            context.ReportDiagnostic(Diagnostic.Create(CannotBind, invocation.Syntax.GetLocation(), component.Name, bound.Name, valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void CheckAttribute(OperationAnalysisContext context, IInvocationOperation invocation, INamedTypeSymbol component)
    {
        var argument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "name");
        if (argument?.Value.ConstantValue is not { HasValue: true, Value: string name })
        {
            return;
        }

        var parameters = Parameters(component).ToArray();
        if (parameters.FirstOrDefault(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase) && !CapturesUnmatched(p)) is { } declared)
        {
            context.ReportDiagnostic(Diagnostic.Create(AttributeIsParameter, argument.Syntax.GetLocation(), component.Name, declared.Name));
        }
        else if (!parameters.Any(CapturesUnmatched))
        {
            context.ReportDiagnostic(Diagnostic.Create(NoUnmatchedValues, argument.Syntax.GetLocation(), component.Name, name));
        }
    }

    private static void CollectStoryName(IInvocationOperation invocation, List<(string Name, string Slug, Location Location)> names)
    {
        var method = invocation.TargetMethod;
        if (method.Name != "Add" || method.ContainingType?.OriginalDefinition.ToDisplayString() != CollectionBuilderType)
        {
            return;
        }

        var argument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "name");
        if (argument?.Value.ConstantValue is { HasValue: true, Value: string name })
        {
            lock (names)
            {
                names.Add((name, Slugify(name), argument.Syntax.GetLocation()));
            }
        }
    }

    private static void ReportDuplicates(OperationBlockAnalysisContext context, List<(string Name, string Slug, Location Location)> names)
    {
        foreach (var group in names.GroupBy(n => n.Slug, System.StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var duplicate in group.OrderBy(n => n.Location.SourceSpan.Start).Skip(1))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateStory, duplicate.Location, duplicate.Name, duplicate.Slug));
            }
        }
    }

    // c => c.Property (optionally converted): the shape StoryBuilder accepts.
    private static IPropertySymbol? SelectedProperty(IOperation? value)
    {
        while (value is IConversionOperation or IDelegateCreationOperation)
        {
            value = value is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)value).Target;
        }

        if (value is not IAnonymousFunctionOperation lambda || lambda.Body.Operations.Length != 1 || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } returned })
        {
            return null;
        }

        while (returned is IConversionOperation conversion)
        {
            returned = conversion.Operand;
        }

        return returned is IPropertyReferenceOperation { Instance: IParameterReferenceOperation } reference ? reference.Property : null;
    }

    private static bool IsParameter(IPropertySymbol property)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is ParameterAttribute or CascadingParameterAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPropertySymbol> Parameters(INamedTypeSymbol component)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (var type = component; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic && IsParameter(property) && seen.Add(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool CapturesUnmatched(IPropertySymbol property) =>
        property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ParameterAttribute
            && a.NamedArguments.Any(n => n.Key == "CaptureUnmatchedValues" && n.Value.Value is true));

    // Mirrors StoryId.Slugify.
    internal static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingDash = false;
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.Length == 0 ? "story" : builder.ToString();
    }
}
