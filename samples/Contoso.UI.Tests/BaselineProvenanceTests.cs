using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Visual;

namespace Contoso.UI.Tests;

/// <summary>
/// CI compares against chromium-linux baselines. When Microsoft.Playwright is upgraded, the browser build changes and so
/// do screenshots; this fails fast, before any browser starts, and says how to regenerate them.
/// </summary>
public sealed class BaselineProvenanceTests
{
    [Fact]
    public void Linux_baselines_come_from_the_pinned_playwright_version()
    {
        var settings = BtpSettings.Current;
        var manifest = BaselineManifest.Load(settings.SnapshotDirectoryPath);
        if (manifest.Get("chromium-linux") is not { } recorded)
        {
            Assert.Skip("No chromium-linux baselines are recorded yet (run scripts/visual-tests.sh --update).");
            return;
        }

        Assert.True(
            recorded.Playwright == BrowserStoryRunner.PlaywrightVersion,
            $"The chromium-linux baselines were recorded with Playwright {recorded.Playwright}, but the tests now use {BrowserStoryRunner.PlaywrightVersion}. "
            + "Regenerate them: run the \"Regenerate visual baselines\" workflow on this branch, or scripts/visual-tests.sh --update=changed, and review the diffs.");
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
