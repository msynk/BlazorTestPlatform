using System.Text;
using System.Text.Json;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Workbench;

/// <summary>Serializes an in-app play run for the workbench shell and browser tests (trimming safe).</summary>
internal static class PlayResult
{
    public static string ToJson(string status, string? error, IReadOnlyList<StepRecord> steps, int actions, string renderer)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("status", status);
            if (error is not null)
            {
                writer.WriteString("error", error);
            }

            writer.WriteString("renderer", renderer);
            writer.WriteNumber("actions", actions);
            writer.WritePropertyName("steps");
            writer.WriteRawValue(JsonSerializer.Serialize(steps.ToList(), BtpJson.TypeInfo<List<StepRecord>>()));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
