using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class StoryAttachmentsTests
{
    [Fact]
    public void Clean_passes_attach_nothing_and_changes_attach_their_evidence()
    {
        var directory = Directory.CreateTempSubdirectory("btp-attachments-").FullName;
        try
        {
            string File(string name)
            {
                var path = Path.Combine(directory, name);
                System.IO.File.WriteAllBytes(path, [0]);
                return path;
            }

            var passed = new StoryResult
            {
                Key = "k", StoryId = "s", Title = "T", Name = "N", Driver = "playwright", Outcome = StoryOutcome.Passed,
                Artifacts = [new ArtifactReference("screenshot", File("final.png"))],
            };
            Assert.Empty(StoryAttachments.For(passed));

            var changed = passed with
            {
                Visual = new VisualResult { SnapshotName = "x", Status = VisualStatus.Mismatched, BaselinePath = File("b.png"), ActualPath = File("a.png"), DiffPath = File("d.png") },
                Artifacts = [new ArtifactReference("screenshot", File("s1.png")), new ArtifactReference("screenshot", File("s2.png")), new ArtifactReference("trace", File("trace.zip")), new ArtifactReference("log", Path.Combine(directory, "missing.txt"))],
            };
            var attachments = StoryAttachments.For(changed);

            Assert.Equal(["baseline.png", "actual.png", "diff.png", "screenshot.png", "screenshot-2.png", "trace.zip"], attachments.Select(a => a.Name));
            Assert.Equal("application/zip", attachments[^1].MediaType);
            Assert.All(attachments, a => Assert.True(Path.IsPathFullyQualified(a.Path)));
            Assert.Equal(3, StoryAttachments.For(passed with { Outcome = StoryOutcome.Flaky, Artifacts = changed.Artifacts }).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
