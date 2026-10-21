using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BlazorTestPlatform.Workbench.Components;

/// <summary>Reflection-free helpers for inline frame scripts.</summary>
internal static class FrameScripts
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Default };

    /// <summary>Writes a string dictionary as a JSON object that is safe to embed in a script element.</summary>
    public static string ToJsonObject(IReadOnlyDictionary<string, string> values)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in values)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
