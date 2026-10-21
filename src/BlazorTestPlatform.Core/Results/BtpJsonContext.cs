using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Performance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorTestPlatform.Results;

/// <summary>Source-generated metadata for every file format BlazorTestPlatform reads or writes (trimming safe).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoryResult))]
[JsonSerializable(typeof(PerformanceBaselineStore.PerformanceBaselineDocument))]
[JsonSerializable(typeof(BtpSettings.SettingsFile))]
[JsonSerializable(typeof(ComponentReview))]
[JsonSerializable(typeof(List<StepRecord>))]
internal sealed partial class BtpJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for action-log arguments: primitives and Blazor event arguments.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<ActionRecord>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(ChangeEventArgs))]
[JsonSerializable(typeof(MouseEventArgs))]
[JsonSerializable(typeof(PointerEventArgs))]
[JsonSerializable(typeof(WheelEventArgs))]
[JsonSerializable(typeof(DragEventArgs))]
[JsonSerializable(typeof(KeyboardEventArgs))]
[JsonSerializable(typeof(FocusEventArgs))]
[JsonSerializable(typeof(ClipboardEventArgs))]
[JsonSerializable(typeof(ProgressEventArgs))]
[JsonSerializable(typeof(TouchEventArgs))]
internal sealed partial class ActionJsonContext : JsonSerializerContext;

/// <summary>Builds resolvers: generated metadata first, reflection only where it is enabled (never in trimmed apps).</summary>
internal static class JsonResolvers
{
    public static IJsonTypeInfoResolver Create(IJsonTypeInfoResolver generated) =>
        JsonSerializer.IsReflectionEnabledByDefault ? JsonTypeInfoResolver.Combine(generated, Reflection()) : generated;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only used when reflection-based serialization is enabled (JsonSerializer.IsReflectionEnabledByDefault), which trimmed apps disable.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only used when reflection-based serialization is enabled.")]
    private static IJsonTypeInfoResolver Reflection() => new DefaultJsonTypeInfoResolver();

    public static JsonTypeInfo<T> TypeInfo<T>(this JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
