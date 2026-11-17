using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Visual;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// CI compares against Linux baselines. When Microsoft.Playwright is upgraded, the browser build changes and so do
/// screenshots; this fails with one clear message that says how to regenerate them (CI runs it as its own step before
/// the browser suites).
/// </summary>
public sealed class BaselineProvenanceTests
{
    [Fact]
    public void Linux_baselines_come_from_the_pinned_playwright_version()
    {
        var settings = BtpSettings.Current;
        var manifest = BaselineManifest.Load(settings.SnapshotDirectoryPath);
        var linux = manifest.Entries.Values.Where(e => e.Platform == "linux").ToArray();
        if (linux.Length == 0)
        {
            Assert.Skip("No Linux baselines are recorded yet (run scripts/visual-tests.sh --update).");
            return;
        }

        var outdated = linux.Where(e => e.Playwright != BrowserStoryRunner.PlaywrightVersion).Select(e => $"{e.Key} (Playwright {e.Playwright})").ToArray();
        Assert.True(
            outdated.Length == 0,
            $"Baselines were recorded with another Playwright version than the {BrowserStoryRunner.PlaywrightVersion} the tests now use: {string.Join(", ", outdated)}. "
            + "Regenerate them: run the \"Regenerate visual baselines\" workflow on this branch, or scripts/visual-tests.sh --update=changed, and review the diffs.");
    }

    [Fact]
    public void Every_baseline_belongs_to_a_story_that_takes_snapshots()
    {
        // Renamed, removed and NoVisual() stories leave baselines nobody compares against (Jest's "obsolete snapshots").
        var store = SnapshotStore.FromSettings(BtpSettings.Current);
        var orphans = store.FindOrphanedBaselines(ComponentStoryTests.Catalog.Stories)
            .Select(p => Path.GetRelativePath(store.BaselineDirectory, p))
            .ToArray();

        Assert.True(orphans.Length == 0, $"Baselines without a story: {string.Join(", ", orphans)}. Delete them, or call SnapshotStore.PruneOrphanedBaselines(catalog.Stories).");
    }

    [Fact]
    public void Recorded_version_is_the_pinned_package_version()
    {
        var props = FindUp(BtpSettings.Current.ProjectDirectory, "Directory.Packages.props");
        if (props is null)
        {
            Assert.Skip("Not running inside the repository.");
            return;
        }

        var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(props), "Include=\"Microsoft\\.Playwright\"\\s+Version=\"([^\"]+)\"");
        Assert.True(match.Success, "Microsoft.Playwright is not pinned in Directory.Packages.props.");
        Assert.Equal(match.Groups[1].Value, BrowserStoryRunner.PlaywrightVersion);
    }

    private static string? FindUp(string directory, string fileName)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
