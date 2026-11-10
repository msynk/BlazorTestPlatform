using System.Net;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Demo.Workbench;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>The guided tour on the demo's home page must never point at a story or a file that no longer exists.</summary>
public sealed class TourTests
{
    [Fact]
    public void Every_tour_link_opens_a_story()
    {
        var missing = Tour.StoryIds.Where(id => !ComponentStoryTests.Catalog.TryGet(id, out _)).ToArray();
        Assert.True(missing.Length == 0, "The tour links to stories that don't exist: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_file_the_tour_names_exists()
    {
        var root = RepositoryRoot();
        if (root is null)
        {
            Assert.Skip("Not running inside the repository.");
            return;
        }

        var missing = Tour.CodePaths.Where(p => !File.Exists(Path.Combine(root, p)) && !Directory.Exists(Path.Combine(root, p))).ToArray();
        Assert.True(missing.Length == 0, "The tour names files that don't exist: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Home_page_explains_the_platform_and_links_into_the_workbench()
    {
        await using var host = new WebApplicationFactory<Program>();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Run it as every kind of test.", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/_workbench?story=overlays-dialog--keyboard&amp;play=1\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tour-missing", body, StringComparison.Ordinal);
    }

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(BtpSettings.Current.ProjectDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlazorTestPlatform.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
