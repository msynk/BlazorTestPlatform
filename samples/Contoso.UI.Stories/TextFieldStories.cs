using BlazorTestPlatform;

namespace Contoso.UI.Stories;

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

        stories.Add("Empty", s => s.Arg(f => f.Placeholder, "you@example.com"));
        stories.Add("With hint", s => s.Arg(f => f.Hint, "We never share your address."));
        stories.Add("Required", s => s.Arg(f => f.Required, true));
        stories.Add("Invalid", s => s
            .Bind(f => f.Value, "ada@")
            .Arg(f => f.Error, "Enter a complete email address.")
            .Play(async ctx =>
            {
                var field = ctx.GetByLabel("Email");
                await ctx.Expect(field).ToHaveAttributeAsync("aria-invalid", "true");
                await ctx.Expect(field).ToHaveAttributeAsync("aria-describedby", "field-email-error");
            }));

        stories.Add("Typing updates the bound value", s => s.Play(async ctx =>
        {
            var field = ctx.GetByLabel("Email");
            await field.FillAsync("ada@example.com");
            await ctx.Expect(field).ToHaveValueAsync("ada@example.com");
            await ctx.ExpectAction("ValueChanged").ToHaveBeenLastCalledWithAsync("ada@example.com");
        }));
    }
}
