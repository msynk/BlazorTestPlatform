using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorTestPlatform;

/// <summary>
/// Renders a <see cref="Story"/>: its decorators, fake authentication and the component under test.
/// Every driver renders stories through this component, so a story behaves identically in bUnit,
/// the workbench and browser runs.
/// </summary>
public sealed class StoryRoot : ComponentBase, IDisposable
{
    private Task<AuthenticationState>? _authenticationState;
    private StoryRenderContext? _subscribedContext;

    /// <summary>The render context created by the driver.</summary>
    [Parameter, EditorRequired]
    public StoryRenderContext Context { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribedContext, Context))
        {
            return;
        }

        Unsubscribe();
        _subscribedContext = Context;
        Context.AttachRoot(this);

        if (Context.Authentication is { } authentication)
        {
            _authenticationState = authentication.GetAuthenticationStateAsync();
            authentication.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }
        else
        {
            _authenticationState = null;
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var fragment = Context.Story.Render(Context);
        if (_authenticationState is null)
        {
            builder.AddContent(0, fragment);
            return;
        }

        builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(1);
        builder.AddComponentParameter(2, nameof(CascadingValue<Task<AuthenticationState>>.Value), _authenticationState);
        builder.AddComponentParameter(3, nameof(CascadingValue<Task<AuthenticationState>>.ChildContent), fragment);
        builder.CloseComponent();
    }

    /// <summary>Re-renders the story (used after external state changes).</summary>
    public Task RefreshAsync() => InvokeAsync(StateHasChanged);

    /// <inheritdoc />
    public void Dispose() => Unsubscribe();

    private void OnAuthenticationStateChanged(Task<AuthenticationState> state) =>
        _ = InvokeAsync(() =>
        {
            _authenticationState = state;
            StateHasChanged();
        });

    private void Unsubscribe()
    {
        if (_subscribedContext?.Authentication is { } authentication)
        {
            authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }
    }
}

/// <summary>
/// A neutral host for composition stories that render several components together:
/// <c>public sealed class LoginFormStories : Stories&lt;Composition&gt;</c>.
/// </summary>
public sealed class Composition : ComponentBase
{
    /// <summary>The composed content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}
