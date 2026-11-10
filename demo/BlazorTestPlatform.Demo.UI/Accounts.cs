using System.Net;
using System.Net.Http.Json;

namespace BlazorTestPlatform.Demo.UI;

public sealed record SignUpRequest(string Name, string Email);

public enum SignUpOutcome
{
    Created,
    EmailTaken,
}

/// <summary>Typed HTTP client for the accounts API (source-generated JSON: trimming safe).</summary>
public sealed class AccountsClient(HttpClient http)
{
    /// <summary>Creates an account. Throws <see cref="HttpRequestException"/> when the server can't be reached or fails.</summary>
    public async Task<SignUpOutcome> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/accounts", request, DemoJsonContext.Default.SignUpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return SignUpOutcome.EmailTaken;
        }

        response.EnsureSuccessStatusCode();
        return SignUpOutcome.Created;
    }
}
