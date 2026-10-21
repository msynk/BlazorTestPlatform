using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace BlazorTestPlatform;

/// <summary>A group of stories sharing a title (one component).</summary>
/// <param name="Title">The shared title.</param>
/// <param name="ComponentType">The component type.</param>
/// <param name="Stories">Stories in declaration order.</param>
public sealed record StoryGroup(string Title, Type ComponentType, IReadOnlyList<Story> Stories)
{
    /// <summary>Title segments, e.g. <c>["Inputs", "Button"]</c>.</summary>
    public IReadOnlyList<string> Segments { get; } = Title.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Slug of the title; used for review files and reports.</summary>
    public string Key { get; } = StoryId.Slugify(Title);
}

/// <summary>
/// An immutable, validated set of stories discovered from assemblies or collections.
/// Feed it to test frameworks (<see cref="Ids"/>), the workbench and the browser matrix.
/// </summary>
public sealed class StoryCatalog
{
    private static readonly ConcurrentDictionary<string, StoryCatalog> AssemblyCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Story> _byId;

    /// <summary>Creates a catalog. Throws when two stories share an id.</summary>
    public StoryCatalog(IEnumerable<Story> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);
        Stories = stories.ToArray();
        _byId = new Dictionary<string, Story>(StringComparer.Ordinal);
        foreach (var story in Stories)
        {
            if (!_byId.TryAdd(story.Id, story))
            {
                var existing = _byId[story.Id];
                throw new InvalidOperationException(
                    $"Duplicate story id '{story.Id}': '{existing.Path}' ({existing.CollectionType?.Name}) and '{story.Path}' ({story.CollectionType?.Name}). Rename one story or change a collection Title.");
            }
        }

        Groups = Stories
            .GroupBy(s => s.Title, StringComparer.Ordinal)
            .Select(g => new StoryGroup(g.Key, g.First().ComponentType, g.OrderBy(s => s.Order).ToArray()))
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>All stories.</summary>
    public IReadOnlyList<Story> Stories { get; }

    /// <summary>Stories grouped by title, ordered by title.</summary>
    public IReadOnlyList<StoryGroup> Groups { get; }

    /// <summary>Number of stories.</summary>
    public int Count => Stories.Count;

    /// <summary>All story ids (handy for <c>[MemberData]</c>/<c>[TestCaseSource]</c>; strings serialize in every test framework).</summary>
    public IEnumerable<string> Ids => Stories.Select(s => s.Id);

    /// <summary>Gets a story by id.</summary>
    public Story this[string id] => Get(id);

    /// <summary>
    /// Discovers every <see cref="StoryCollection"/> (and <see cref="IStoryConfiguration"/>) with a public parameterless
    /// constructor in the assemblies. Types nested in private or protected scopes are ignored (use
    /// <see cref="FromCollections"/> for those). Assemblies compiled with BlazorTestPlatform carry a source-generated
    /// provider, so discovery needs no reflection and works in trimmed apps; other assemblies are scanned. Cached per
    /// assembly set.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Assemblies compiled with BlazorTestPlatform.Core get a generated, trim-safe provider; the reflection scan is only a fallback for assemblies built without the generator.")]
    public static StoryCatalog FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var key = string.Join('|', assemblies.Select(a => a.FullName).Order(StringComparer.Ordinal));
        return AssemblyCache.GetOrAdd(key, _ => Discover(assemblies, useGeneratedProviders: true));
    }

    /// <summary>Discovery through reflection only (ignores generated providers). Used to verify the generator.</summary>
    [RequiresUnreferencedCode("Scans assemblies with reflection; story types may be trimmed.")]
    internal static StoryCatalog FromAssembliesByReflection(params Assembly[] assemblies) => Discover(assemblies, useGeneratedProviders: false);

    /// <summary>True when the assembly carries a source-generated catalog provider.</summary>
    public static bool HasGeneratedProvider(Assembly assembly) =>
        (assembly ?? throw new ArgumentNullException(nameof(assembly))).GetCustomAttribute<StoryCatalogProviderAttribute>() is not null;

    /// <summary>Discovers stories in the assembly declaring <typeparamref name="T"/>.</summary>
    public static StoryCatalog FromAssemblyOf<T>() => FromAssemblies(typeof(T).Assembly);

    /// <summary>Builds a catalog from explicit collections (no reflection; trimming/AOT friendly).</summary>
    public static StoryCatalog FromCollections(IEnumerable<StoryCollection> collections, IEnumerable<IStoryConfiguration>? configurations = null)
    {
        ArgumentNullException.ThrowIfNull(collections);
        var global = configurations?.ToArray() ?? [];
        return new StoryCatalog(collections.SelectMany(c => c.Build(global)));
    }

    /// <summary>Gets a story by id or throws with suggestions.</summary>
    public Story Get(string id)
    {
        if (TryGet(id, out var story))
        {
            return story;
        }

        var suggestions = Stories
            .Select(s => s.Id)
            .Where(s => s.Contains(id, StringComparison.OrdinalIgnoreCase) || id.Contains(s, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToArray();
        throw new KeyNotFoundException(
            $"Story '{id}' was not found." + (suggestions.Length > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : string.Empty));
    }

    /// <summary>Tries to get a story by id.</summary>
    public bool TryGet(string id, [NotNullWhen(true)] out Story? story) => _byId.TryGetValue(id, out story);

    /// <summary>Returns a filtered catalog.</summary>
    public StoryCatalog Where(Func<Story, bool> predicate) => new(Stories.Where(predicate));

    /// <summary>Stories carrying a tag.</summary>
    public StoryCatalog WithTag(string tag) => Where(s => s.HasTag(tag));

    /// <summary>Stories not carrying a tag.</summary>
    public StoryCatalog WithoutTag(string tag) => Where(s => !s.HasTag(tag));

    /// <summary>Stories that have a play function.</summary>
    public StoryCatalog WithPlay() => Where(s => s.HasPlay);

    /// <summary>Stories of one component type.</summary>
    public StoryCatalog ForComponent<TComponent>() => Where(s => s.ComponentType == typeof(TComponent));

    /// <summary>Stories whose id or path matches a glob (<c>*</c>, <c>?</c>), e.g. <c>inputs-*</c>.</summary>
    public StoryCatalog Matching(string pattern) => Where(s => Glob.IsMatch(s.Id, pattern) || Glob.IsMatch(s.Path, pattern));

    [RequiresUnreferencedCode("Falls back to scanning assemblies without a generated provider.")]
    private static StoryCatalog Discover(Assembly[] assemblies, bool useGeneratedProviders)
    {
        var collections = new List<StoryCollection>();
        var configurations = new List<IStoryConfiguration>();
        foreach (var assembly in assemblies.Distinct())
        {
            var attribute = useGeneratedProviders ? assembly.GetCustomAttribute<StoryCatalogProviderAttribute>() : null;
            if (attribute is not null)
            {
                var provider = (IStoryCatalogProvider)Activator.CreateInstance(attribute.ProviderType)!;
                collections.AddRange(provider.CreateCollections());
                configurations.AddRange(provider.CreateConfigurations());
            }
            else
            {
                Scan(assembly, collections, configurations);
            }
        }

        // One global order (by type name) whatever the discovery path, so generated and scanned catalogs are identical.
        var orderedConfigurations = configurations.OrderBy(c => c.GetType().FullName, StringComparer.Ordinal).ToArray();
        return new StoryCatalog(collections
            .OrderBy(c => c.GetType().FullName, StringComparer.Ordinal)
            .SelectMany(c => c.Build(orderedConfigurations)));
    }

    [RequiresUnreferencedCode("Scans assemblies with reflection; story types may be trimmed.")]
    private static void Scan(Assembly assembly, List<StoryCollection> collections, List<IStoryConfiguration> configurations)
    {
        foreach (var type in GetLoadableTypes(assembly).Where(IsDiscoverable))
        {
            if (typeof(IStoryConfiguration).IsAssignableFrom(type))
            {
                configurations.Add((IStoryConfiguration)Activator.CreateInstance(type)!);
            }

            if (typeof(StoryCollection).IsAssignableFrom(type))
            {
                collections.Add((StoryCollection)Activator.CreateInstance(type)!);
            }
        }
    }

    /// <summary>The selection rules shared with the source generator.</summary>
    [RequiresUnreferencedCode("Inspects constructors with reflection.")]
    internal static bool IsDiscoverable(Type type)
    {
        if (type is not { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } || type.GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.IsNestedPrivate || current.IsNestedFamily || current.IsNestedFamANDAssem)
            {
                return false;
            }
        }

        return typeof(StoryCollection).IsAssignableFrom(type) || typeof(IStoryConfiguration).IsAssignableFrom(type);
    }

    [RequiresUnreferencedCode("Enumerates assembly types.")]
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

/// <summary>Minimal glob matcher (<c>*</c> and <c>?</c>, case-insensitive).</summary>
internal static class Glob
{
    public static bool IsMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
