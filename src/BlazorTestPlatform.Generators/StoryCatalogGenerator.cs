using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BlazorTestPlatform.Generators;

/// <summary>
/// Emits a reflection-free <c>IStoryCatalogProvider</c> for every assembly that declares story collections or story
/// configurations, plus an assembly attribute pointing at it. <c>StoryCatalog.FromAssemblies</c> prefers the provider,
/// which keeps discovery working in trimmed (and AOT-compiled) apps. The selection rules mirror runtime discovery
/// exactly: concrete, non-generic classes (records included) with a public parameterless constructor that are not
/// nested in private or protected scopes, not file-local and not obsolete as errors.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StoryCatalogGenerator : IIncrementalGenerator
{
    private const string CollectionType = "BlazorTestPlatform.StoryCollection";
    private const string ConfigurationType = "BlazorTestPlatform.IStoryConfiguration";
    private const string ProviderAttribute = "BlazorTestPlatform.StoryCatalogProviderAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                static (syntax, cancellationToken) => Describe(syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, cancellationToken) as INamedTypeSymbol))
            .Where(static type => type is not null)
            .Select(static (type, _) => type!.Value)
            .Collect();

        var assembly = context.CompilationProvider.Select(static (compilation, _) => (
            Name: compilation.AssemblyName ?? "Stories",
            HasCore: compilation.GetTypeByMetadataName(ProviderAttribute) is not null));

        context.RegisterSourceOutput(candidates.Combine(assembly), static (output, input) =>
        {
            var (types, (assemblyName, hasCore)) = input;
            if (!hasCore || types.IsDefaultOrEmpty)
            {
                return;
            }

            var distinct = types
                .GroupBy(t => t.MetadataName, System.StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(t => t.MetadataName, System.StringComparer.Ordinal)
                .ToArray();

            output.AddSource("BlazorTestPlatform.StoryCatalog.g.cs", SourceText.From(Emit(assemblyName, distinct), Encoding.UTF8));
        });
    }

    private static DiscoveredType? Describe(INamedTypeSymbol? symbol)
    {
        if (symbol is null || symbol.TypeKind != TypeKind.Class || symbol.IsAbstract || symbol.IsStatic || symbol.IsGenericType || symbol.IsFileLocal)
        {
            return null;
        }

        // Generated code cannot reference a type obsolete as an error (CS0619); runtime discovery skips it too.
        if (symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute"
                && a.ConstructorArguments.Length == 2 && a.ConstructorArguments[1].Value is true))
        {
            return null;
        }

        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType || current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return null;
            }
        }

        if (!symbol.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
        {
            return null;
        }

        var isCollection = false;
        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.OriginalDefinition.ToDisplayString() == CollectionType)
            {
                isCollection = true;
                break;
            }
        }

        var isConfiguration = symbol.AllInterfaces.Any(i => i.ToDisplayString() == ConfigurationType);
        if (!isCollection && !isConfiguration)
        {
            return null;
        }

        // 'new T()' does not compile for types with required members (CS9035); Activator does what reflection would do.
        var hasRequiredMembers = false;
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                hasRequiredMembers = true;
                break;
            }
        }

        var constructor = symbol.InstanceConstructors.First(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        var setsRequired = constructor.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

        // Where the class is declared, for reports, SARIF results and the workbench (the first part of a partial class).
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan();
        return new DiscoveredType(
            MetadataName(symbol),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            isCollection,
            isConfiguration,
            hasRequiredMembers && !setsRequired,
            location?.Path,
            location is { } span ? span.StartLinePosition.Line + 1 : 0);
    }

    private static string MetadataName(INamedTypeSymbol symbol)
    {
        var names = new Stack<string>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() + "." : string.Empty;
        return ns + string.Join("+", names);
    }

    private static string Emit(string assemblyName, IReadOnlyList<DiscoveredType> types)
    {
        // A namespace segment cannot start with a digit ("1Stories"): prefix those with an underscore.
        var segment = new string(assemblyName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        var ns = "BlazorTestPlatform.Generated." + (segment.Length == 0 || char.IsDigit(segment[0]) ? "_" + segment : segment);
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable CS0612, CS0618 // Obsolete story types are still stories.");
        builder.AppendLine($"[assembly: global::{ProviderAttribute}(typeof(global::{ns}.GeneratedStoryCatalog))]");
        builder.AppendLine();
        builder.AppendLine($"namespace {ns}");
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>Reflection-free story discovery generated by BlazorTestPlatform.</summary>");
        builder.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"BlazorTestPlatform.Generators\", \"1.0\")]");
        builder.AppendLine("    internal sealed class GeneratedStoryCatalog : global::BlazorTestPlatform.IStoryCatalogProvider");
        builder.AppendLine("    {");
        builder.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<global::BlazorTestPlatform.StoryCollection> CreateCollections() =>");
        builder.AppendLine("            new global::BlazorTestPlatform.StoryCollection[]");
        builder.AppendLine("            {");
        foreach (var type in types.Where(t => t.IsCollection))
        {
            builder.AppendLine(type.SourceFile is { Length: > 0 } file
                ? $"                global::BlazorTestPlatform.StoryCollection.WithSource({Create(type)}, @\"{file.Replace("\"", "\"\"")}\", {type.SourceLine}),"
                : $"                {Create(type)},");
        }

        builder.AppendLine("            };");
        builder.AppendLine();
        builder.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<global::BlazorTestPlatform.IStoryConfiguration> CreateConfigurations() =>");
        builder.AppendLine("            new global::BlazorTestPlatform.IStoryConfiguration[]");
        builder.AppendLine("            {");
        foreach (var type in types.Where(t => t.IsConfiguration))
        {
            builder.AppendLine($"                {Create(type)},");
        }

        builder.AppendLine("            };");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string Create(DiscoveredType type) =>
        type.HasRequiredMembers ? $"global::System.Activator.CreateInstance<{type.DisplayName}>()" : $"new {type.DisplayName}()";

    private readonly struct DiscoveredType : System.IEquatable<DiscoveredType>
    {
        public DiscoveredType(string metadataName, string displayName, bool isCollection, bool isConfiguration, bool hasRequiredMembers, string? sourceFile, int sourceLine)
        {
            MetadataName = metadataName;
            DisplayName = displayName;
            IsCollection = isCollection;
            IsConfiguration = isConfiguration;
            HasRequiredMembers = hasRequiredMembers;
            SourceFile = sourceFile;
            SourceLine = sourceLine;
        }

        public string? SourceFile { get; }

        public int SourceLine { get; }

        public bool HasRequiredMembers { get; }

        public string MetadataName { get; }

        public string DisplayName { get; }

        public bool IsCollection { get; }

        public bool IsConfiguration { get; }

        public bool Equals(DiscoveredType other) =>
            MetadataName == other.MetadataName && DisplayName == other.DisplayName && IsCollection == other.IsCollection && IsConfiguration == other.IsConfiguration
            && HasRequiredMembers == other.HasRequiredMembers && SourceFile == other.SourceFile && SourceLine == other.SourceLine;

        public override bool Equals(object? obj) => obj is DiscoveredType other && Equals(other);

        public override int GetHashCode() => MetadataName.GetHashCode();
    }
}
