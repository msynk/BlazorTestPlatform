namespace BlazorTestPlatform.Playwright;

/// <summary>JavaScript snippets evaluated in the page.</summary>
internal static class Scripts
{
    /// <summary>
    /// Imports the workbench's locator engine (<c>btp-driver.js</c>) into the story frame. The in-app driver uses the same
    /// module, and it ports the in-memory engine's roles, accessible names, text and query rules, so all three drivers
    /// resolve locators alike.
    /// </summary>
    private const string Engine = "(await import(new URL('_content/BlazorTestPlatform.Workbench/btp-driver.js', document.baseURI).href))";

    /// <summary>Describes every element a locator chain (JSON) matches, as JSON.</summary>
    public const string Query = "async (steps) => " + Engine + ".queryJson(steps)";

    /// <summary>Returns the single element an action targets, or the problem to retry on (a string).</summary>
    public const string ActionTarget = "async ([steps, action]) => " + Engine + ".actionTargetJson(steps, action)";

    /// <summary>What Check/Uncheck must do on an element: <c>click</c>, nothing (null) or the problem.</summary>
    public const string CheckPlan = "async (element, want) => " + Engine + ".checkPlan(element, want)";

    /// <summary>Checked state, as JSON, of the last action's target (the locator chain re-resolves it after a re-render).</summary>
    public const string TargetChecked = "async (steps) => " + Engine + ".targetCheckedJson(steps)";

    /// <summary>Describes the focused story element as JSON (null for the body or elements outside the story).</summary>
    public const string Focused = "async () => " + Engine + ".focusedJson()";

    /// <summary>CSS injected before snapshots: no animations, transitions or carets.</summary>
    public const string StabilizeCss = "*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;transition-duration:0s!important;transition-delay:0s!important;caret-color:transparent!important;scroll-behavior:auto!important}";

    /// <summary>Waits for fonts and images.</summary>
    public const string WaitForAssets = """
        async () => {
          if (document.fonts && document.fonts.ready) { await document.fonts.ready; }
          const images = Array.from(document.images).filter(i => !i.complete);
          await Promise.all(images.map(i => new Promise(r => { i.addEventListener('load', r, { once: true }); i.addEventListener('error', r, { once: true }); })));
          await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          return true;
        }
        """;

    /// <summary>Returns the visible Blazor error UI text, if any.</summary>
    public const string BlazorErrorUi = """
        () => {
          const ui = document.getElementById('blazor-error-ui');
          if (!ui) return null;
          const style = getComputedStyle(ui);
          return style.display !== 'none' && style.visibility !== 'hidden' ? (ui.textContent || 'Blazor error UI is visible').trim() : null;
        }
        """;
}
