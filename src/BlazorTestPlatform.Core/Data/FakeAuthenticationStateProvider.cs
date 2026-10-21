using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorTestPlatform;

/// <summary>
/// An <see cref="AuthenticationStateProvider"/> driven by a <see cref="TestUser"/>.
/// Switch users at runtime with <see cref="SetUser"/> to test sign-in/sign-out transitions.
/// </summary>
public sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
{
    private TestUser _user;
    private TaskCompletionSource<AuthenticationState>? _pending;

    /// <summary>Creates the provider for an initial user (anonymous when null).</summary>
    public FakeAuthenticationStateProvider(TestUser? user = null)
    {
        _user = user ?? TestUser.Anonymous;
    }

    /// <summary>The current user.</summary>
    public TestUser User => _user;

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => CreateState(_user);

    /// <summary>Replaces the user and notifies subscribers (e.g. <c>CascadingAuthenticationState</c>).</summary>
    public void SetUser(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
        var state = CreateState(user);
        if (!user.IsAuthenticating && _pending is { } pending)
        {
            _pending = null;
            pending.TrySetResult(new AuthenticationState(user.ToClaimsPrincipal()));
        }

        NotifyAuthenticationStateChanged(state);
    }

    /// <summary>Builds an authentication-state task for a user; never completes while authenticating.</summary>
    internal Task<AuthenticationState> CreateState(TestUser user)
    {
        if (user.IsAuthenticating)
        {
            _pending ??= new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }

        return Task.FromResult(new AuthenticationState(user.ToClaimsPrincipal()));
    }

    /// <summary>Creates the authentication-state task for a user without a provider instance.</summary>
    public static Task<AuthenticationState> CreateStateFor(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsAuthenticating
            ? new TaskCompletionSource<AuthenticationState>().Task
            : Task.FromResult(new AuthenticationState(user.ToClaimsPrincipal()));
    }
}
