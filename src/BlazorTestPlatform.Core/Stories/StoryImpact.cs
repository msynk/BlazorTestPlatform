using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>
/// Test impact analysis: which stories a set of changed files can affect, so a pull request runs those first (or only
/// those). A story is affected when its collection's source file changed, when a file named after its component changed
/// (<c>Button.razor</c>, <c>Button.razor.cs</c>, <c>Button.razor.css</c>, <c>Button.razor.js</c>...), when a component it
/// renders (directly or through other components) is affected, or when a changed file carries its id (a baseline).
/// Any other changed source or build file makes <see cref="RunsEverything"/> true: shared code can affect any story, so
/// the analysis never narrows the run on a change it cannot place. Documentation and images outside baselines are ignored.
/// </summary>
/// <remarks>
/// Component dependencies come from the compiled render code (<c>OpenComponent&lt;T&gt;</c> calls). Components chosen at
/// run time (<c>DynamicComponent</c>, render fragments passed by stories) are not seen: run everything on main.
/// </remarks>
public sealed class StoryImpact
{
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".pdf", ".log", ".trx",
    };

    private StoryImpact(IReadOnlyList<Story> stories, bool runsEverything, IReadOnlyList<string> unmapped, IReadOnlyDictionary<string, IReadOnlyList<string>> reasons)
    {
        Stories = stories;
        RunsEverything = runsEverything;
        UnmappedFiles = unmapped;
        Reasons = reasons;
    }

    /// <summary>The stories to run: the affected ones, or every story when <see cref="RunsEverything"/>.</summary>
    public IReadOnlyList<Story> Stories { get; }

    /// <summary>True when a changed file could not be mapped to stories and may affect any of them.</summary>
    public bool RunsEverything { get; }

    /// <summary>Changed source or build files no story could be traced to (why <see cref="RunsEverything"/> is true).</summary>
    public IReadOnlyList<string> UnmappedFiles { get; }

    /// <summary>For each affected story id, the changed files that affect it.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Reasons { get; }

    /// <summary>Ids of <see cref="Stories"/> (for <c>[MemberData]</c> and friends).</summary>
    public IEnumerable<string> Ids => Stories.Select(s => s.Id);

    /// <summary>Maps changed files (paths relative to the repository or absolute) to the stories they affect.</summary>
    /// <param name="catalog">The stories.</param>
    /// <param name="changedFiles">Changed files, for example from <see cref="ChangedFilesSince"/>.</param>
    [RequiresUnreferencedCode("Reads the IL of component render methods to find the components they render.")]
    public static StoryImpact Analyze(StoryCatalog catalog, IEnumerable<string> changedFiles)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var files = changedFiles.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var reasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        var dependents = Dependents(catalog.Stories.Select(s => s.ComponentType).Distinct());

        foreach (var file in files)
        {
            var hits = new HashSet<Story>();

            // The collection that declares the story.
            hits.UnionWith(catalog.Stories.Where(s => s.SourceFile is { Length: > 0 } source && SamePath(Normalize(source), file)));

            // A file named after a component: that component and every component that renders it.
            var name = Path.GetFileName(file);
            var stem = name.Split('.')[0];
            var components = dependents.Keys.Where(t => string.Equals(ComponentName(t), stem, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (components.Length > 0 && IsComponentFile(name))
            {
                var affected = Closure(components, dependents);
                hits.UnionWith(catalog.Stories.Where(s => affected.Contains(s.ComponentType)));
            }

            // Files that carry a story id, in their name or as a directory: baselines are stored as
            // {story-id}/{viewport}.{theme}.{browser}-{platform}.png; reviews and results.
            var directories = file.Split('/')[..^1];
            hits.UnionWith(catalog.Stories.Where(s =>
                name.Contains(s.Id, StringComparison.OrdinalIgnoreCase)
                || directories.Contains(s.Id, StringComparer.OrdinalIgnoreCase)));

            if (hits.Count == 0)
            {
                if (!IgnoredExtensions.Contains(Path.GetExtension(file)))
                {
                    unmapped.Add(file);
                }

                continue;
            }

            foreach (var story in hits)
            {
                (reasons.TryGetValue(story.Id, out var list) ? list : reasons[story.Id] = []).Add(file);
            }
        }

        var runsEverything = unmapped.Count > 0;
        var stories = runsEverything ? catalog.Stories : catalog.Stories.Where(s => reasons.ContainsKey(s.Id)).ToArray();
        return new StoryImpact(stories, runsEverything, unmapped, reasons.ToDictionary(r => r.Key, r => (IReadOnlyList<string>)r.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Files changed between <paramref name="baseRef"/> and the working tree (committed, staged and unstaged), from
    /// <c>git diff --name-only &lt;merge-base&gt;</c>, relative to the repository root. Null when git is unavailable or the
    /// reference is unknown: callers should then run everything.
    /// </summary>
    [UnsupportedOSPlatform("browser")]
    public static IReadOnlyList<string>? ChangedFilesSince(string baseRef, string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);
        if (baseRef.StartsWith('-'))
        {
            throw new ArgumentException("A git reference cannot start with '-'.", nameof(baseRef));
        }

        var mergeBase = Git(workingDirectory, "merge-base", baseRef, "HEAD")?.Trim();
        if (string.IsNullOrEmpty(mergeBase))
        {
            return null;
        }

        var output = Git(workingDirectory, "diff", "--name-only", "--no-renames", mergeBase);
        // ls-files only lists the current directory: ":/" widens it to the whole working tree.
        var untracked = Git(workingDirectory, "ls-files", "--others", "--exclude-standard", "--full-name", "--", ":/");
        return output is null
            ? null
            : [.. (output + "\n" + untracked).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The stories to run for this checkout: when <c>BTP_CHANGED_SINCE</c> names a git reference (for example
    /// <c>origin/main</c> on pull-request builds), the stories affected since it; otherwise, or when git cannot answer,
    /// every story.
    /// </summary>
    [RequiresUnreferencedCode("Reads the IL of component render methods to find the components they render.")]
    [UnsupportedOSPlatform("browser")]
    public static StoryImpact FromEnvironment(StoryCatalog catalog, string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var since = Environment.GetEnvironmentVariable("BTP_CHANGED_SINCE");
        var changed = string.IsNullOrWhiteSpace(since) ? null : ChangedFilesSince(since.Trim(), workingDirectory);
        return changed is null
            ? new StoryImpact(catalog.Stories, runsEverything: true, [], new Dictionary<string, IReadOnlyList<string>>())
            : Analyze(catalog, changed);
    }

    private static bool IsComponentFile(string name) =>
        name.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
        || name.Contains(".razor.", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static string ComponentName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim();

    // Relative paths (git output) match the end of absolute ones (compiler paths), on a directory boundary.
    private static bool SamePath(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var (longer, shorter) = a.Length >= b.Length ? (a, b) : (b, a);
        return !Path.IsPathRooted(shorter) && longer.EndsWith("/" + shorter.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<Type> Closure(IEnumerable<Type> changed, Dictionary<Type, HashSet<Type>> dependents)
    {
        var result = new HashSet<Type>();
        var queue = new Queue<Type>(changed);
        while (queue.TryDequeue(out var type))
        {
            if (result.Add(type) && dependents.TryGetValue(type, out var users))
            {
                foreach (var user in users)
                {
                    queue.Enqueue(user);
                }
            }
        }

        return result;
    }

    // Component -> components that render it, over every component type in the assemblies of the story components.
    [RequiresUnreferencedCode("Reads the IL of component render methods.")]
    private static Dictionary<Type, HashSet<Type>> Dependents(IEnumerable<Type> storyComponents)
    {
        var assemblies = storyComponents.Select(t => t.Assembly).Distinct().ToArray();
        var components = assemblies.SelectMany(LoadableTypes).Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        foreach (var type in storyComponents.Where(t => !components.Contains(t)))
        {
            components.Add(type);
        }

        var dependents = components.ToDictionary(t => t, _ => new HashSet<Type>());
        foreach (var user in components)
        {
            foreach (var used in RenderedComponents(user))
            {
                var key = used.IsConstructedGenericType ? used.GetGenericTypeDefinition() : used;
                if (key != user && dependents.TryGetValue(key, out var users))
                {
                    users.Add(user);
                }
            }
        }

        return dependents;
    }

    [RequiresUnreferencedCode("Lists every type of the assembly.")]
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    // Finds OpenComponent<T>() calls in the type's methods (and its nested types: lambdas, render fragments), and in the
    // type-inference helpers Razor generates for the generic components it renders with inferred type arguments
    // (__Blazor.{namespace}.{component}.TypeInference.CreateGrid_0<TItem>, which call OpenComponent<Grid<TItem>>).
    [RequiresUnreferencedCode("Resolves IL metadata tokens.")]
    private static HashSet<Type> RenderedComponents(Type type)
    {
        var found = new HashSet<Type>();
        var inference = type.Assembly.GetType($"__Blazor.{(type.Namespace is { } ns ? ns + "." : string.Empty)}{ComponentName(type)}.TypeInference");
        var types = new[] { type, inference }
            .OfType<Type>()
            .SelectMany(t => t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Prepend(t));
        foreach (var current in types)
        {
            var methods = current.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                byte[]? il;
                try
                {
                    il = method.GetMethodBody()?.GetILAsByteArray();
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or BadImageFormatException)
                {
                    continue;
                }

                if (il is null)
                {
                    continue;
                }

                // call/callvirt followed by a MethodSpec token (table 0x2B): generic method instantiations.
                for (var i = 0; i + 4 < il.Length; i++)
                {
                    if ((il[i] != 0x28 && il[i] != 0x6F) || il[i + 4] != 0x2B)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        var genericArguments = current.IsGenericType ? current.GetGenericArguments() : null;
                        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                        if (current.Module.ResolveMethod(token, genericArguments, methodArguments) is MethodInfo { IsGenericMethod: true, Name: "OpenComponent" } open
                            && open.DeclaringType?.FullName == "Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder")
                        {
                            found.Add(open.GetGenericArguments()[0]);
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or MissingMethodException)
                    {
                        // Not a token after all (the bytes were an operand of another instruction).
                    }
                }
            }
        }

        return found;
    }

    [UnsupportedOSPlatform("browser")]
    private static string? Git(string? workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = errors.Result;
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
