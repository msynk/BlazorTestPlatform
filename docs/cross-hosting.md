# Cross-hosting matrix

A Blazor component can work under Interactive Server and break under WebAssembly. The usual causes are a missing service in the client, a type that does not serialize, a JS-interop timing difference, or code that is not trimming-safe. A component can also look different during static SSR prerendering. The matrix runs each story under every render mode from **one** workbench host.

## Dimensions

```csharp
var matrix = new TestMatrix()
    .WithHostingModels(HostingModel.StaticServer, HostingModel.InteractiveServer, HostingModel.InteractiveWebAssembly, HostingModel.InteractiveAuto)
    .WithViewports(Viewport.Desktop, Viewport.Mobile)
    .WithThemes(ThemeDefinition.Light, ThemeDefinition.Dark)
    .WithBrowsers(Browsers.Chromium, Browsers.Firefox);

IEnumerable<MatrixCase> cells = matrix.Expand(catalog);   // story filters prune cells
string key = cell.Key;                                    // "story|viewport|theme|mode|browser"
MatrixCase back = matrix.Resolve(catalog, key);           // serializable for any test framework
```

Presets: `TestMatrix.Default()` (desktop and mobile, light, Server) and `TestMatrix.CrossHosting()` (all four modes on desktop). Stories narrow the matrix with `.OnlyHostingModels(...)`, `.ExcludeHostingModels(...)` and `.Matrix(m => m.Viewports.Add("mobile"))`.

## What a browser run checks

1. Navigates to `/_workbench/frame/{id}?mode=…&theme=…&viewport=…` in a fresh, isolated browser context. The locale is `en-US`, the time zone is UTC, motion is reduced, and the color scheme comes from the theme.
2. Waits for `data-btp-status="ready"`. For interactive modes it also waits for `data-btp-interactive="true"`, which the story host sets only after hydration and once the frame bridge is registered. This prevents **"clicked before the circuit/runtime was live"**, the classic Blazor flake.
3. Verifies the renderer that actually ran. A story requested as WebAssembly must report `WebAssembly`.
4. Runs the play script. Static SSR cannot handle events, so play is skipped there, and the snapshot uses the `initial` baseline.
5. Takes a visual snapshot. Baselines exclude the render mode, so every mode is compared against the **same** image.
6. Runs an axe-core scan.
7. Fails the run on uncaught page errors, console errors (configurable allow-list), a visible `#blazor-error-ui`, or an error the story's own error boundary caught (`[data-btp-error]`, also after hydration).

**Timeouts and cancellation.** Each attempt has a hard limit (`BrowserRunOptions.Timeout`, else the story's `.Timeout(...)`, else 60 s) that covers navigation, play, snapshot and axe, not just the play script: a story that blocks the browser (for example an endless loop in WebAssembly) fails as *timed out* instead of hanging the test run. Cancelling the run (the test framework's token) is not a failure: the runner stops retrying, records nothing and throws `OperationCanceledException`. A browser that crashed is relaunched for the next cell, and failure diagnostics (screenshot, DOM, trace) are each bounded to 10 s, so a hung page still produces a result.

**Visual evidence.** Every render mode and attempt keeps its own actual and diff images (`TestResults/btp/visual/{story}/{name}.{mode}-attempt-{n}.actual.png`), so a mismatch under WebAssembly stays in the report when Server then matches, and a flaky run keeps the images of its failed attempt.

Each interaction then round-trips through the frame bridge (`window.__btp.waitForIdle()`), so Server render batches and WebAssembly renders are applied before the next step.

## Prerendering

Interactive modes are prerendered, as in production. If a story's first render never completes (a pending request, an authorizing user), Blazor cannot hydrate it. Call `.Prerender(false)` on such stories; the error message suggests this when it happens. Story frames use streaming rendering, so loading states also render under static SSR.

## Hosts

| Host | Use when |
|---|---|
| `WorkbenchApplicationFactory<Program>` | Default. In-process on Kestrel (a real socket for browsers). Override `ConfigureWebHost` to swap services for every story. |
| `ProcessWorkbenchHost("path/to/Workbench.csproj")` | Runs `dotnet run` on a free port, exactly like a developer. |
| `ExternalWorkbenchHost(new Uri(...))` or `BTP_BASE_URL` | A running dev server, container or preview deployment. |

## Trimmed (Release) publishes

The source generator emits a reflection-free story catalog, and Core and the workbench are trimming-safe: they build with zero trim warnings. To verify a real trimmed WebAssembly publish, point the browser suite at it:

```bash
dotnet publish samples/Contoso.Workbench -c Release -o out
(cd out && dotnet Contoso.Workbench.dll --urls http://127.0.0.1:5288 &)
BTP_BASE_URL=http://127.0.0.1:5288 dotnet test --project samples/Contoso.UI.Tests -- --filter-class '*CrossHostingTests' --filter-class '*InAppPlayTests'
```

CI runs the same check in the `trimmed-publish` job, a little stricter: the publish treats trim warnings as errors, the app runs with `ASPNETCORE_ENVIRONMENT=Production`, and the live-controls tests (`*ArgControlsTests`) run against it too. It caught a real bug before release: the trimmer removed the story host from WebAssembly clients, because the server requests it by name only.

## Hand-written browser tests

```csharp
await using var page = await environment.OpenAsync(cell);
var ctx = page.CreateStoryContext();
await ctx.GetByRole("button", "Open").ClickAsync();
await page.Page.Mouse.WheelAsync(0, 400);   // full Playwright API when you need it
```
