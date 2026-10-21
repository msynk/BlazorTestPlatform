using System.Security.Claims;

namespace BlazorTestPlatform;

/// <summary>
/// A fake user for stories. The same definition authenticates the story in bUnit,
/// in the workbench and in real-browser runs (through a cascading <c>Task&lt;AuthenticationState&gt;</c>).
/// </summary>
public sealed record TestUser
{
    /// <summary>Authentication type reported by fake identities.</summary>
    public const string DefaultAuthenticationType = "BlazorTestPlatform";

    /// <summary>An anonymous (unauthenticated) user.</summary>
    public static TestUser Anonymous { get; } = new() { IsAuthenticated = false, Name = null };

    /// <summary>The user name (<see cref="ClaimTypes.Name"/>).</summary>
    public string? Name { get; init; }

    /// <summary>Whether the identity is authenticated.</summary>
    public bool IsAuthenticated { get; init; } = true;

    /// <summary>Authentication type of the identity.</summary>
    public string AuthenticationType { get; init; } = DefaultAuthenticationType;

    /// <summary>Roles (<see cref="ClaimTypes.Role"/>).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Additional claims as type/value pairs.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Claims { get; init; } = [];

    /// <summary>When true, the authentication state never completes (shows <c>Authorizing</c> content).</summary>
    public bool IsAuthenticating { get; init; }

    /// <summary>Creates an authenticated user with the given name.</summary>
    public static TestUser Named(string name) => new() { Name = name };

    /// <summary>A user whose authentication is still in progress.</summary>
    public static TestUser Authenticating { get; } = new() { IsAuthenticating = true, IsAuthenticated = false };

    /// <summary>Returns a copy with additional roles.</summary>
    public TestUser WithRoles(params string[] roles) => this with { Roles = [.. Roles, .. roles] };

    /// <summary>Returns a copy with an additional claim.</summary>
    public TestUser WithClaim(string type, string value) => this with { Claims = [.. Claims, new(type, value)] };

    /// <summary>Builds the <see cref="ClaimsPrincipal"/> for this user.</summary>
    public ClaimsPrincipal ToClaimsPrincipal()
    {
        if (!IsAuthenticated)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>();
        if (Name is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, Name));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, Name));
        }

        claims.AddRange(Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(Claims.Select(c => new Claim(c.Key, c.Value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <inheritdoc />
    public override string ToString() => IsAuthenticating ? "(authenticating)" : IsAuthenticated ? Name ?? "(authenticated)" : "(anonymous)";
}
