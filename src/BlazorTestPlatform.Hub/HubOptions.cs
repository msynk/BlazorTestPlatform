using System.Reflection;
using BlazorTestPlatform.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Hub;

/// <summary>Configures the hub: where the results, baselines and run archive of a test project live.</summary>
public sealed class HubOptions
{
    /// <summary>Route of the hub.</summary>
    public const string RoutePrefix = "/_btp";

    /// <summary>Title shown in the header.</summary>
    public string Title { get; set; } = "Test hub";

    /// <summary>
    /// The test project directory (holds <c>btp.json</c>, results, baselines, reviews and <c>quarantine.json</c>).
    /// Defaults to <c>BTP_PROJECT_DIR</c>, else the current directory.
    /// </summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Run archive directory. Default: <c>{ProjectDirectory}/.btp/runs</c>.</summary>
    public string? ArchiveDirectory { get; set; }

    /// <summary>Runs analysed for stability, clusters and trends.</summary>
    public int Window { get; set; } = 30;

    /// <summary>Runs kept in the archive.</summary>
    public int ArchiveCapacity { get; set; } = 100;

    /// <summary>
    /// Whether the hub responds. Null (the default): everywhere except in a <c>Production</c> host, unless
    /// <c>BTP_HUB_ENABLED</c> says otherwise. The hub reads test output, writes baselines and quarantine entries and runs
    /// stories, so a deployed host must opt in explicitly, ideally with an <see cref="AuthorizationPolicy"/>.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>Authorization policy a user must satisfy to use the hub. Users who fail it get 404.</summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>Whether the hub may change files (accept baselines, quarantine stories) and start runs. Default true.</summary>
    public bool AllowChanges { get; set; } = true;

    /// <summary>Tag of stories the hub runs but the workbench does not host (no "open in workbench" links).</summary>
    public const string HubOnlyTag = "hub-only";

    /// <summary>Base URL of the component workbench, for "open in workbench" links; null hides them.</summary>
    public string? WorkbenchUrl { get; set; } = "/_workbench";

    /// <summary>Repository web URL (<c>https://github.com/org/repo</c>), for commit and source links.</summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>Assemblies whose stories the hub can run in memory.</summary>
    public List<Assembly> StoryAssemblies { get; } = [];

    /// <summary>Explicit story collections the hub can run in memory.</summary>
    public List<StoryCollection> Collections { get; } = [];

    /// <summary>Whether the pages respond, as resolved by <c>AddBlazorTestHub</c>.</summary>
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>Adds the assembly declaring <typeparamref name="T"/> to the runnable stories.</summary>
    public HubOptions AddStoriesFromAssemblyOf<T>()
    {
        if (!StoryAssemblies.Contains(typeof(T).Assembly))
        {
            StoryAssemblies.Add(typeof(T).Assembly);
        }

        return this;
    }

    /// <summary>The project directory, resolved.</summary>
    public string ResolvedProjectDirectory =>
        Path.GetFullPath(ProjectDirectory
            ?? (Environment.GetEnvironmentVariable("BTP_PROJECT_DIR") is { Length: > 0 } fromEnvironment ? fromEnvironment : Environment.CurrentDirectory));

    /// <summary>The archive directory, resolved.</summary>
    public string ResolvedArchiveDirectory => Path.GetFullPath(ArchiveDirectory ?? Path.Combine(ResolvedProjectDirectory, ".btp", "runs"));

    /// <summary>Loads the project's settings (fresh each call, so edits to btp.json and quarantine.json apply).</summary>
    public BtpSettings LoadSettings() => BtpSettings.Load(ResolvedProjectDirectory);

    internal StoryCatalog BuildCatalog()
    {
        var stories = new List<Story>();
        if (StoryAssemblies.Count > 0)
        {
            stories.AddRange(StoryCatalog.FromAssemblies([.. StoryAssemblies]).Stories);
        }

        if (Collections.Count > 0)
        {
            stories.AddRange(StoryCatalog.FromCollections(Collections).Stories);
        }

        return new StoryCatalog(stories.GroupBy(s => s.Id, StringComparer.Ordinal).Select(g => g.First()));
    }
}

/// <summary>Builds hub URLs.</summary>
public static class HubUrls
{
    /// <summary>A hub page.</summary>
    public static string Page(string path = "") => HubOptions.RoutePrefix + (path.Length == 0 ? string.Empty : "/" + path.TrimStart('/'));

    /// <summary>The page of one result.</summary>
    public static string Result(string key) => Page("result?key=" + Uri.EscapeDataString(key));

    /// <summary>The page of one story.</summary>
    public static string Story(string storyId) => Page("tests/" + Uri.EscapeDataString(storyId));

    /// <summary>The page of one run.</summary>
    public static string Run(string runId) => Page("runs/" + Uri.EscapeDataString(runId));

    /// <summary>A comparison of two runs.</summary>
    public static string Compare(string baseRunId, string headRunId) =>
        Page($"compare?base={Uri.EscapeDataString(baseRunId)}&head={Uri.EscapeDataString(headRunId)}");
}

internal static class HubAccess
{
    public static async Task<bool> IsAllowedAsync(HubOptions options, IServiceProvider services, System.Security.Claims.ClaimsPrincipal? user = null)
    {
        if (!options.IsEnabled)
        {
            return false;
        }

        if (options.AuthorizationPolicy is not { Length: > 0 } policy)
        {
            return true;
        }

        if (services.GetService<IAuthorizationService>() is not { } authorization)
        {
            return false;
        }

        try
        {
            if (user is null)
            {
                if (services.GetService<AuthenticationStateProvider>() is not { } authentication)
                {
                    return false;
                }

                user = (await authentication.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
            }

            return (await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
