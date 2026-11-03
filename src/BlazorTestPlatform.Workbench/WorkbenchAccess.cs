using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Workbench;

/// <summary>Decides whether the current request may see workbench pages (see <see cref="WorkbenchOptions.Enabled"/>).</summary>
internal static class WorkbenchAccess
{
    /// <summary>
    /// True when the workbench is enabled for this host and, if <see cref="WorkbenchOptions.AuthorizationPolicy"/> is
    /// set, the current user satisfies it. Fails closed: a policy that cannot be evaluated (no authorization services or
    /// no authentication state) denies access.
    /// </summary>
    public static async Task<bool> IsAllowedAsync(WorkbenchOptions options, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        if (!options.IsEnabled)
        {
            return false;
        }

        if (options.AuthorizationPolicy is not { Length: > 0 } policy)
        {
            return true;
        }

        if (services.GetService<IAuthorizationService>() is not { } authorization || services.GetService<AuthenticationStateProvider>() is not { } authentication)
        {
            return false;
        }

        try
        {
            var state = await authentication.GetAuthenticationStateAsync().ConfigureAwait(false);
            return (await authorization.AuthorizeAsync(state.User, policy).ConfigureAwait(false)).Succeeded;
        }
        catch (InvalidOperationException)
        {
            // Unknown policy or no authentication state for this request: deny.
            return false;
        }
    }
}
