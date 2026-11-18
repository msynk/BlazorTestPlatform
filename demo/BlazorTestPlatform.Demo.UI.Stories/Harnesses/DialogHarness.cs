using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories.Harnesses;

/// <summary>
/// The test API of the design system's <see cref="Dialog"/> (a component harness): tests confirm, cancel and check the
/// dialog through it, not through its markup, in every driver.
/// </summary>
public sealed class DialogHarness : ComponentHarness
{
    /// <inheritdoc />
    protected override By HostQuery => By.Role("dialog");

    private ElementLocator Actions => Locate(By.Css(".c-dialog__actions button"));

    /// <summary>The dialog's title.</summary>
    public Task<string> GetTitleAsync() => Locate(By.Css(".c-dialog__title")).TextAsync();

    /// <summary>Presses the confirming action (the last one).</summary>
    public Task ConfirmAsync() => Actions.Last.ClickAsync();

    /// <summary>Presses the cancelling action (the first one).</summary>
    public Task CancelAsync() => Actions.First.ClickAsync();

    /// <summary>The dialog is open, and named <paramref name="title"/> when given.</summary>
    public async Task ExpectOpenAsync(string? title = null)
    {
        await Context.Expect(Host).ToBeVisibleAsync();
        if (title is not null)
        {
            await Context.Expect(Host).ToHaveAccessibleNameAsync(title);
        }
    }

    /// <summary>The dialog is closed.</summary>
    public Task ExpectClosedAsync() => Context.Expect(Host).ToHaveCountAsync(0);
}
