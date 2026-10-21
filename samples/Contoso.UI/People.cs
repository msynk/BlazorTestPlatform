using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contoso.UI;

public sealed record Person(string Name, string Email, string Team);

/// <summary>Typed HTTP client for the people API (source-generated JSON: trimming safe).</summary>
public sealed class PeopleClient(HttpClient http)
{
    public async Task<IReadOnlyList<Person>> GetAsync(string? team, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrEmpty(team) ? "api/people" : $"api/people?team={Uri.EscapeDataString(team)}";
        return await http.GetFromJsonAsync(url, ContosoJsonContext.Default.PersonArray, cancellationToken).ConfigureAwait(false) ?? [];
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Person[]))]
public sealed partial class ContosoJsonContext : JsonSerializerContext;
