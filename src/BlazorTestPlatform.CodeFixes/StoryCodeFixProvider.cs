using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorTestPlatform.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace BlazorTestPlatform.CodeFixes;

/// <summary>
/// Fixes for the story analyzers: BTP001 adds <c>[Parameter]</c> to the selected property, BTP002 turns a binding into
/// a one-way <c>Arg</c>, BTP003 turns <c>Attribute("Name", value)</c> into <c>Arg(c =&gt; c.Name, value)</c>, and BTP005
/// gives a duplicate story the next free name.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StoryCodeFixProvider)), Shared]
public sealed class StoryCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
        StoryAnalyzer.NotAParameter.Id, StoryAnalyzer.CannotBind.Id, StoryAnalyzer.AttributeIsParameter.Id, StoryAnalyzer.DuplicateStory.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            switch (diagnostic.Id)
            {
                case "BTP001" when node.FirstAncestorOrSelf<ArgumentSyntax>() is { } argument:
                    await RegisterAddParameterAsync(context, diagnostic, argument).ConfigureAwait(false);
                    break;
                case "BTP002" when node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { Expression: MemberAccessExpressionSyntax } invocation:
                    context.RegisterCodeFix(
                        CodeAction.Create("Use Arg (one-way value)", ct => ReplaceAsync(context.Document, invocation, BindToArg(invocation), ct), "BTP002"),
                        diagnostic);
                    break;
                case "BTP003" when diagnostic.Properties.TryGetValue("Parameter", out var parameter) && parameter is not null
                    && node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { Expression: MemberAccessExpressionSyntax } invocation
                    && AttributeToArg(invocation, parameter) is { } replacement:
                    context.RegisterCodeFix(
                        CodeAction.Create($"Use Arg(c => c.{parameter}, value)", ct => ReplaceAsync(context.Document, invocation, replacement, ct), "BTP003"),
                        diagnostic);
                    break;
                case "BTP005" when node.FirstAncestorOrSelf<ArgumentSyntax>() is { Expression: LiteralExpressionSyntax literal } argument
                    && literal.IsKind(SyntaxKind.StringLiteralExpression):
                    var name = NextFreeName(argument, literal.Token.ValueText);
                    context.RegisterCodeFix(
                        CodeAction.Create($"Rename to '{name}'", ct => ReplaceAsync(context.Document, literal,
                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)).WithTriviaFrom(literal), ct), "BTP005"),
                        diagnostic);
                    break;
            }
        }
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode oldNode, SyntaxNode newNode, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode));
    }

    // s.Bind(c => c.Value, "x") -> s.Arg(c => c.Value, "x")
    private static InvocationExpressionSyntax BindToArg(InvocationExpressionSyntax invocation)
    {
        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var name = access.Name is GenericNameSyntax generic
            ? (SimpleNameSyntax)generic.WithIdentifier(SyntaxFactory.Identifier("Arg").WithTriviaFrom(generic.Identifier))
            : SyntaxFactory.IdentifierName(SyntaxFactory.Identifier("Arg").WithTriviaFrom(access.Name.Identifier));
        var arguments = invocation.ArgumentList.Arguments.Select(a =>
            a.NameColon?.Name.Identifier.ValueText == "initialValue" ? a.WithNameColon(SyntaxFactory.NameColon("value")) : a);
        return invocation
            .WithExpression(access.WithName(name))
            .WithArgumentList(invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments, invocation.ArgumentList.Arguments.GetSeparators())))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    // s.Attribute("Title", "x") -> s.Arg(c => c.Title, "x")
    private static InvocationExpressionSyntax? AttributeToArg(InvocationExpressionSyntax invocation, string parameter)
    {
        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var arguments = invocation.ArgumentList.Arguments;
        var value = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "value")
            ?? arguments.Where(a => a.NameColon is null).Skip(1).FirstOrDefault();
        if (value is null)
        {
            return null;
        }

        var lambdaParameter = FreeIdentifier(invocation, "c", "component", "x");
        var selector = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(lambdaParameter)),
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(lambdaParameter), SyntaxFactory.IdentifierName(parameter)));
        return invocation
            .WithExpression(access.WithName(SyntaxFactory.IdentifierName(SyntaxFactory.Identifier("Arg").WithTriviaFrom(access.Name.Identifier))))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(selector),
                value.WithNameColon(null),
            })).WithTriviaFrom(invocation.ArgumentList))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    // Adds [Parameter] to the selected property, when it is a public, settable property declared in source.
    private static async Task RegisterAddParameterAsync(CodeFixContext context, Diagnostic diagnostic, ArgumentSyntax argument)
    {
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var body = argument.Expression switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
            _ => null,
        };
        if (model is null || body is not ExpressionSyntax expression
            || model.GetSymbolInfo(expression, context.CancellationToken).Symbol is not IPropertySymbol property
            || property.DeclaredAccessibility != Accessibility.Public || property.SetMethod?.DeclaredAccessibility != Accessibility.Public
            || model.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ParameterAttribute") is not { } attribute)
        {
            return;
        }

        var reference = property.DeclaringSyntaxReferences.FirstOrDefault();
        var declaringDocument = reference is null ? null : context.Document.Project.Solution.GetDocument(reference.SyntaxTree);
        if (reference is null || declaringDocument is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create($"Add [Parameter] to '{property.ContainingType.Name}.{property.Name}'", async ct =>
            {
                var editor = await DocumentEditor.CreateAsync(declaringDocument, ct).ConfigureAwait(false);
                var declaration = await reference.GetSyntaxAsync(ct).ConfigureAwait(false);
                var generator = editor.Generator;
                editor.AddAttribute(declaration, generator.Attribute(generator.TypeExpression(attribute).WithAdditionalAnnotations(Simplifier.Annotation)));
                return editor.GetChangedDocument().Project.Solution;
            }, "BTP001"),
            diagnostic);
    }

    // "Default" -> "Default 2" (or the next number whose story id is free in this method).
    private static string NextFreeName(ArgumentSyntax argument, string name)
    {
        var scope = (SyntaxNode?)argument.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() ?? argument.SyntaxTree.GetRoot();
        var taken = new HashSet<string>(scope.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" })
            .Select(i => i.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => StoryAnalyzer.Slugify(l.Token.ValueText)), System.StringComparer.Ordinal);

        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (!taken.Contains(StoryAnalyzer.Slugify(candidate)))
            {
                return candidate;
            }
        }
    }

    // A lambda parameter name that no enclosing lambda, method parameter or local of the method already declares.
    private static string FreeIdentifier(SyntaxNode near, params string[] candidates)
    {
        var used = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var ancestor in near.Ancestors())
        {
            switch (ancestor)
            {
                case SimpleLambdaExpressionSyntax simple:
                    used.Add(simple.Parameter.Identifier.ValueText);
                    break;
                case ParenthesizedLambdaExpressionSyntax parenthesized:
                    used.UnionWith(parenthesized.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
                    break;
                case BaseMethodDeclarationSyntax method:
                    used.UnionWith(method.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
                    used.UnionWith(method.DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(v => v.Identifier.ValueText));
                    used.UnionWith(method.DescendantNodes().OfType<ForEachStatementSyntax>().Select(f => f.Identifier.ValueText));
                    break;
            }
        }

        return candidates.FirstOrDefault(c => !used.Contains(c)) ?? candidates[0] + "1";
    }
}
