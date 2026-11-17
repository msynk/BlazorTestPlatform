using BlazorTestPlatform.Configuration;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Visual.Tests;

public sealed class OrphanedBaselineTests
{
    [Fact]
    public void Baselines_of_removed_and_no_visual_stories_are_orphans_and_can_be_pruned()
    {
        var root = Directory.CreateTempSubdirectory("btp-orphans-").FullName;
        try
        {
            var baselines = Path.Combine(root, "__snapshots__");
            foreach (var name in new[] { "chips-chip--default/desktop.light.chromium-linux.png", "chips-chip--hidden/desktop.light.chromium-linux.png", "chips-chip--renamed/desktop.light.chromium-linux.png", "chips-chip--renamed/mobile.light.chromium-linux.initial.png" })
            {
                var path = Path.Combine(baselines, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [0]);
            }

            File.WriteAllText(Path.Combine(baselines, "baselines.json"), "{}");
            var store = new SnapshotStore(baselines, Path.Combine(root, "out"), SnapshotUpdateMode.None);
            var stories = StoryCatalog.FromCollections([new ChipStories()]).Stories;

            var orphans = store.FindOrphanedBaselines(stories);

            Assert.Equal(
                [
                    Path.Combine(baselines, "chips-chip--hidden", "desktop.light.chromium-linux.png"),
                    Path.Combine(baselines, "chips-chip--renamed", "desktop.light.chromium-linux.png"),
                    Path.Combine(baselines, "chips-chip--renamed", "mobile.light.chromium-linux.initial.png"),
                ],
                orphans);

            Assert.Equal(orphans, store.PruneOrphanedBaselines(stories));
            Assert.Empty(store.FindOrphanedBaselines(stories));
            Assert.False(Directory.Exists(Path.Combine(baselines, "chips-chip--renamed")));
            Assert.True(File.Exists(Path.Combine(baselines, "chips-chip--default", "desktop.light.chromium-linux.png")));
            Assert.True(File.Exists(Path.Combine(baselines, "baselines.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public sealed class Chip : ComponentBase
    {
        [Parameter] public string? Label { get; set; }
    }

    private sealed class ChipStories : Stories<Chip>
    {
        public override string Title => "Chips/Chip";

        protected override void Define(StoryCollectionBuilder<Chip> stories)
        {
            stories.Add("Default");
            stories.Add("Hidden", s => s.NoVisual());
        }
    }
}
