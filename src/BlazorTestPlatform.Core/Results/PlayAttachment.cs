namespace BlazorTestPlatform.Results;

/// <summary>
/// A file a play script attached to its run (<c>ctx.Attach(...)</c>, Playwright's <c>testInfo.attach</c>): a JSON dump of
/// a service's state, a generated document, a log. Runners write it next to the run's other artifacts, reports link it, and
/// the test framework packages attach it to the test result.
/// </summary>
/// <param name="Name">File name, such as <c>cart.json</c> (made safe for the file system when written).</param>
/// <param name="Content">The bytes.</param>
/// <param name="MediaType">MIME type, such as <c>application/json</c>.</param>
public sealed record PlayAttachment(string Name, ReadOnlyMemory<byte> Content, string MediaType)
{
    /// <summary>The <see cref="ArtifactReference.Kind"/> of attachments in results.</summary>
    public const string ArtifactKind = "attachment";

    /// <summary>Writes <paramref name="attachments"/> into <paramref name="directory"/> and returns them as artifacts.</summary>
    public static IReadOnlyList<ArtifactReference> WriteAll(IReadOnlyList<PlayAttachment> attachments, string directory)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (attachments.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(directory);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<ArtifactReference>();
        foreach (var attachment in attachments)
        {
            var safe = ResultsStore.SafeFileName(attachment.Name);
            var name = safe;
            for (var i = 2; !names.Add(name); i++)
            {
                name = $"{Path.GetFileNameWithoutExtension(safe)}-{i}{Path.GetExtension(safe)}";
            }

            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, attachment.Content.ToArray());
            written.Add(new ArtifactReference(ArtifactKind, path, attachment.Name));
        }

        return written;
    }
}
