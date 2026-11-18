using System.Text.Json.Serialization;

namespace BlazorTestPlatform.Playwright;

/// <summary>Source-generated JSON for what the Playwright package reads back from pages.</summary>
[JsonSerializable(typeof(string[]))]
internal sealed partial class PlaywrightJsonContext : JsonSerializerContext;
