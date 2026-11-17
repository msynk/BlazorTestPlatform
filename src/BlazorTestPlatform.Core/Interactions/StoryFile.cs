using System.Text;

namespace BlazorTestPlatform;

/// <summary>
/// A file a play script chooses in a file input (<see cref="ElementLocator.SetInputFilesAsync"/>), as a person would in
/// the browser's file picker. The content is held in memory, so the same file reaches every driver.
/// </summary>
/// <param name="Name">File name, e.g. <c>avatar.png</c>.</param>
/// <param name="ContentType">MIME type, e.g. <c>image/png</c>.</param>
/// <param name="Content">The file's bytes.</param>
public sealed record StoryFile(string Name, string ContentType, ReadOnlyMemory<byte> Content)
{
    /// <summary>Last-modified time the browser reports (default: 2000-01-01, so runs stay deterministic).</summary>
    public DateTimeOffset LastModified { get; init; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A UTF-8 text file.</summary>
    public static StoryFile FromText(string name, string text, string contentType = "text/plain") =>
        new(name, contentType, Encoding.UTF8.GetBytes(text ?? throw new ArgumentNullException(nameof(text))));

    /// <summary>A file with the given bytes.</summary>
    public static StoryFile FromBytes(string name, byte[] content, string contentType = "application/octet-stream") =>
        new(name, contentType, content ?? throw new ArgumentNullException(nameof(content)));

    /// <summary>A file read from disk now, named after it, with a content type from its extension.</summary>
    public static StoryFile FromPath(string path, string? contentType = null) =>
        new(Path.GetFileName(path), contentType ?? ContentTypeOf(path), File.ReadAllBytes(path));

    /// <summary>The MIME type browsers commonly report for a file extension (<c>application/octet-stream</c> when unknown).</summary>
    public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".html" or ".htm" => "text/html",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({ContentType}, {Content.Length} bytes)";
}
