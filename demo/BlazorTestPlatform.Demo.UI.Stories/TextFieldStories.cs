using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>Two-way binding (<c>Bind</c>), a container decorator, and accessibility wiring checked as attributes.</summary>
public sealed class TextFieldStories : Stories<TextField>
{
    public override string Title => "Inputs/Text field";

    protected override void Define(StoryCollectionBuilder<TextField> stories)
    {
        stories.Defaults(s => s
            .Arg(f => f.Label, "Email")
            .Arg(f => f.Type, "email")
            .Bind(f => f.Value, string.Empty)
            .Container(style: "max-width: 360px"));

        stories.Add("Empty", s => s
            .Description("Bind(...) supplies Value and handles ValueChanged, like @bind-Value in a page. Container(...) is a decorator that constrains the width.")
            .Arg(f => f.Placeholder, "you@example.com"));
        stories.Add("With hint", s => s.Arg(f => f.Hint, "We never share your address."));
        stories.Add("Required", s => s.Arg(f => f.Required, true));
        stories.Add("Invalid", s => s
            .Description("The error is announced with the field: aria-invalid and aria-describedby point at the message.")
            .Bind(f => f.Value, "ada@")
            .Arg(f => f.Error, "Enter a complete email address.")
            .Play(async ctx =>
            {
                var field = ctx.GetByLabel("Email");
                await ctx.Expect(field).ToHaveAttributeAsync("aria-invalid", "true");
                await ctx.Expect(field).ToHaveAttributeAsync("aria-describedby", "field-email-error");
            }));

        stories.Add("Typing updates the bound value", s => s
            .Description("FillAsync fires input events; the binding re-renders with the new value and logs ValueChanged.")
            .Play(async ctx =>
            {
                var field = ctx.GetByLabel("Email");
                await field.FillAsync("ada@example.com");
                await ctx.Expect(field).ToHaveValueAsync("ada@example.com");
                await ctx.ExpectAction("ValueChanged").ToHaveBeenLastCalledWithAsync("ada@example.com");
            }));
    }
}
