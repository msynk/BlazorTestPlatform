using BlazorTestPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// A form that posts to an API, tested end to end without a server: <c>MockHttp</c> answers the POST, and fake time
/// holds a delayed response until the play script moves the clock, so the loading state is checked deterministically
/// instead of raced. Works the same in bUnit, under Interactive Server (the mock runs on the server) and in WebAssembly.
/// </summary>
public sealed class SignUpFormStories : Stories<SignUpForm>
{
    public override string Title => "Patterns/Sign-up form";

    protected override void Define(StoryCollectionBuilder<SignUpForm> stories)
    {
        stories.Defaults(s => s
            .Services(services => services.AddTransient<AccountsClient>())
            .MockHttp(http => http.Post("api/accounts").RespondStatus(201))
            .UseFakeTime(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)));

        stories.Add("Empty", s => s.Description("A pattern composed from the design system's TextField, Button and Alert, with a typed HttpClient behind it."));

        stories.Add("Validation errors", s => s
            .Description("Invalid input never reaches the API: the fields say what is wrong (aria-invalid, aria-describedby) and no request is made.")
            .Play(async ctx =>
            {
                await ctx.GetByRole("textbox", "Email").FillAsync("ada@");
                await ctx.GetByRole("button", "Create account").ClickAsync();

                var name = ctx.GetByRole("textbox", "Name");
                var email = ctx.GetByRole("textbox", "Email");
                await ctx.Expect(name).ToHaveAttributeAsync("aria-invalid", "true");
                await ctx.Expect(name).ToHaveAttributeAsync("aria-describedby", "field-name-error");
                await ctx.Expect(ctx.GetByText("Enter your name.")).ToBeVisibleAsync();
                await ctx.Expect(email).ToHaveAttributeAsync("aria-describedby", "field-email-hint field-email-error");
                await ctx.Expect(ctx.GetByText("Enter a complete email address", exact: false)).ToBeVisibleAsync();
                await ctx.ExpectRequest("POST", "api/accounts").NotToHaveBeenMadeAsync();
            }));

        stories.Add("Successful sign-up", s => s
            .Description("The API answers after 2 seconds of fake time. The play script checks the busy button, moves the clock, then checks the request body, the success message and the OnSignedUp callback.")
            .MockHttp(http => http.Post("api/accounts").Delay(TimeSpan.FromSeconds(2)).RespondStatus(201))
            .Play(async ctx =>
            {
                await ctx.GetByRole("textbox", "Name").FillAsync("Ada Lovelace");
                await ctx.GetByRole("textbox", "Email").FillAsync("ada@example.com");

                await ctx.StepAsync("Enter in a field submits the form, and the response waits for the clock", async () =>
                {
                    await ctx.GetByRole("textbox", "Email").PressAsync(Keys.Enter);
                    var busy = ctx.GetByRole("button", "Creating account");
                    await ctx.Expect(busy).ToBeDisabledAsync();
                    await ctx.Expect(busy).ToHaveAttributeAsync("aria-busy", "true");
                    await ctx.AdvanceTimeAsync(TimeSpan.FromSeconds(2));
                });

                await ctx.Expect(ctx.GetByRole("status")).ToContainTextAsync("We sent a confirmation link to ada@example.com.");
                await ctx.ExpectRequest("POST", "api/accounts").ToHaveBeenLastMadeWithAsync(
                    request => request.Body?.Contains("\"email\":\"ada@example.com\"", StringComparison.Ordinal) == true,
                    "a JSON body with the email address");
                await ctx.ExpectAction("OnSignedUp").ToHaveBeenLastCalledWithAsync("ada@example.com");
            }));

        stories.Add("Email already registered", s => s
            .Description("A 409 from the API becomes a field error, not a generic failure.")
            .MockHttp(http => http.Post("api/accounts").RespondStatus(409))
            .Play(async ctx =>
            {
                await ctx.GetByRole("textbox", "Name").FillAsync("Grace Hopper");
                await ctx.GetByRole("textbox", "Email").FillAsync("grace@example.com");
                await ctx.GetByRole("button", "Create account").ClickAsync();

                var email = ctx.GetByRole("textbox", "Email");
                await ctx.Expect(ctx.GetByText("That email address is already registered.")).ToBeVisibleAsync();
                await ctx.Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
                await ctx.Expect(email).ToHaveValueAsync("grace@example.com");
                await ctx.ExpectAction("OnSignedUp").NotToHaveBeenCalledAsync();
            }));

        stories.Add("Server unreachable", s => s
            .Description("Fail() simulates a network failure: the form keeps the input and explains what happened in an alert.")
            .MockHttp(http => http.Post("api/accounts").Fail())
            .Play(async ctx =>
            {
                await ctx.GetByRole("textbox", "Name").FillAsync("Grace Hopper");
                await ctx.GetByRole("textbox", "Email").FillAsync("grace@example.com");
                await ctx.GetByRole("button", "Create account").ClickAsync();

                await ctx.Expect(ctx.GetByRole("alert")).ToContainTextAsync("could not be reached");
                await ctx.Expect(ctx.GetByRole("textbox", "Name")).ToHaveValueAsync("Grace Hopper");
                await ctx.ExpectRequest("POST", "api/accounts").ToHaveBeenMadeAsync(times: 1);
            }));
    }
}
