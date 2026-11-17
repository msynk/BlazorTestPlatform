# Writing stories

A **story** is one named state of a component. A **collection** (`Stories<TComponent>`) declares the stories of one component. `StoryCatalog.FromAssemblies` (or `FromAssemblyOf<T>`) discovers the collections of an assembly through a source-generated provider, so discovery needs no reflection and works in trimmed apps; assemblies built without the generator are scanned with reflection. `FromCollections` takes an explicit list, for example of collections nested in private classes, which discovery skips.

## Identity

| Concept | Example | Notes |
|---|---|---|
| Title | `Inputs/Button` | Override `Title`. Slashes group components in the workbench sidebar. |
| Name | `Disabled` | The first argument of `Add`. |
| Id | `inputs-button--disabled` | `StoryId.Create(title, name)`. Used in URLs, test names, baselines and results. |

Renaming a title or a story renames its baselines. Run `btp approve` after renames, or move the baseline folders yourself.

## Arguments

```csharp
stories.Add("Danger", s => s
    .Arg(b => b.Variant, ButtonVariant.Danger)             // a parameter value
    .Arg(b => b.Items, ctx => ctx.Fake.Many(5, ...))       // computed once per render context, from seeded data
    .Attribute("data-track", "cta")                        // an unmatched attribute (CaptureUnmatchedValues)
    .ChildContent("Delete project")                        // ChildContent as markup
    .Fragment(b => b.Icon, IconFragment)                   // a RenderFragment parameter
    .Template<Order>(g => g.Row, order => ...));           // a RenderFragment<T> parameter
```

Expressions are validated: `Arg(b => b.NotAParameter, ...)` throws, with a message that tells you to add `[Parameter]`.

### Live controls

The workbench's **Controls** panel edits a story's parameters without touching its code. Every `[Parameter]` of a supported type gets a control: text for `string`, `char`, `Guid`, `Uri`, `TimeSpan`, `DateTimeOffset` and `[Flags]` enums; a checkbox for `bool`; a number input for the numeric types; a drop-down for enums and `bool?`; and date, date-time and time inputs for `DateOnly`, `DateTime` and `TimeOnly`. Nullable variants are supported too. Each control starts from the story's value (or the component's own default) and says which one it is. Callbacks, fragments, cascading parameters, `CaptureUnmatchedValues` and other types aren't editable. The ones the story sets, including its automatic actions, are listed under **Other args**.

Interactive frames (Server, WebAssembly, Auto) re-render in place through the frame bridge, so component state such as focus or an open menu survives an edit. Static SSR frames reload. Two-way bound values take the edited value, and the component can keep changing them.

The edits live in the URL as `?arg.Variant=Danger&arg.Disabled=true`, so a link reproduces exactly what you see. Values equal to the story's own are dropped, `!null` stands for null, and an empty field for a parameter without a default leaves it alone. Values that arrive through a link can't contain `<`, so a shared link can't inject markup into a component that renders a parameter as HTML. Typing such a value into a control of an interactive frame still works, because it reaches the frame through the bridge rather than a URL. Static SSR frames and the form without JavaScript (**Apply**) go through the URL, so they reject it too.

Tests use the same text and the same conversion:

```csharp
var options = new BunitStoryOptions();
options.Args["Variant"] = "Danger";                            // invalid values throw ArgumentException; '<' is allowed
await using var session = await BunitStoryRunner.RenderAsync(story, options);

var page = new StoryPageOptions();
page.Args["Variant"] = "Danger";                               // travels in the frame URL: rejected values (and '<') are listed in [data-btp-arg-errors]
await using var frame = await browser.OpenAsync(cell, page);

await session.RenderContext.SetArgOverridesAsync(new Dictionary<string, object?> { ["Max"] = 4 }); // live, typed
```

`StoryArgs` exposes the rules (`ControlsFor`, `Resolve`, `TryConvert`, `Format`, `FromQuery`/`ToQuery`) for tools that build their own controls.

## Actions (callbacks)

Every `EventCallback`/`EventCallback<T>` parameter that a story does not set is wired to the **action log** automatically. Turn this off with `.AutoActions(false)`. Play scripts assert actions in every driver:

```csharp
await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
await ctx.ExpectAction("ValueChanged").ToHaveBeenLastCalledWithAsync("ada@example.com");
await ctx.ExpectAction("OnDelete").NotToHaveBeenCalledAsync();
```

Arguments are recorded as JSON. In browser runs they cross the frame bridge, so assertions work under Server and WebAssembly too.

## Two-way binding

`Bind` supplies `Value`, handles `ValueChanged` (re-rendering with the new value and logging an action) and supplies `ValueExpression` when the component declares it, which `InputBase` needs:

```csharp
stories.Add("Typing", s => s.Bind(f => f.Value, string.Empty));
```

Form inputs outside an `EditForm` need an edit context: `.WithEditContext(model)`.

## Decorators and layout

```csharp
s.Container(style: "max-width: 360px")                 // wrap in a div
 .CascadingValue(new Density("compact"))               // a cascading value
 .Wrap<MyThemeProvider>()                              // any component that takes ChildContent
 .Decorate((story, ctx) => b => { ... })               // full control
 .Layout(StoryLayout.Centered);                        // workbench layout: Padded | Centered | Fullscreen
```

Decorators added first wrap closest to the component. A theme's `Decorator`, if set, wraps outermost.

## Variants: visual state matrices

```csharp
stories.AddVariants(v => v
    .Vary(b => b.Variant, ButtonVariant.Primary, ButtonVariant.Secondary, ButtonVariant.Danger)
    .Vary(b => b.Size, ButtonSize.Small, ButtonSize.Large),
    s => s.Tags("visual-matrix"));
```

This adds six stories, named like `Variant=Primary, Size=Small`.

**Pairwise variants.** Every combination grows fast: four parameters with four values each are 256 stories. Most rendering and interaction defects involve one or two parameters, so covering every *pair* of values keeps most of the value at a fraction of the cost:

```csharp
stories.AddVariants(v => v
    .Vary(c => c.Size, Size.Small, Size.Medium, Size.Large)
    .Vary(c => c.Tone, Tone.Neutral, Tone.Danger, Tone.Success)
    .Vary(c => c.Disabled, false, true)
    .Vary(c => c.Dense, false, true)
    .Where(c => !(c.Get(x => x.Tone) == Tone.Danger && c.Get(x => x.Disabled)))   // combinations the component does not support
    .Pairwise());
```

`Pairwise()` selects combinations in which every pair of values of any two parameters appears at least once (the In-Parameter-Order strategy). The selection is deterministic, so story ids and baselines never change between runs; with fewer than three parameters it is the full product. `Where(...)` removes combinations from either mode; under `Pairwise()`, pairs that only occur in removed combinations are left uncovered. The same algorithm is public as `Combinatorics.Pairwise(...)` for your own data-driven tests, and `Combinatorics.MissingPairs(...)` checks any set of rows for gaps. A parameter can be varied once per `AddVariants` call; listing it twice is an error.

## Defaults

* **Collection defaults:** `stories.Defaults(s => ...)` applies to every story in the collection, before the story's own configuration.
* **Assembly defaults:** implement `IStoryConfiguration`. Implementations are discovered with the stories and applied first, to every story in the catalog: one `FromAssemblies` call over several assemblies applies all their configurations to all their stories. `FromCollections` and `WorkbenchOptions.Collections` apply only the configurations you pass them.

```csharp
public sealed class Defaults : IStoryConfiguration
{
    public void Configure(IStoryBuilder story) => story.Tags("design-system").Seed(1);
}
```

## Culture and direction

`Culture("de-DE")` renders a story in a culture: dates, numbers and resources format as there. bUnit runs the story under
it (`CultureInfo.CurrentCulture` and `CurrentUICulture`, for render and play, restored afterwards); browser runs use it as
the browser locale (`navigator.language`, `Accept-Language`), which WebAssembly follows and Server or static SSR follow
when the app uses request localization (`app.UseRequestLocalization(...)`). An unknown culture fails when the catalog is
built. For right-to-left languages pair it with the `rtl` theme (`ThemeDefinition.RightToLeft`, see
[quality gates](quality-gates.md#visual-regression)).

```csharp
stories.Add("German totals", s => s.Culture("de-DE").Play(async ctx =>
    await ctx.Expect(ctx.GetByTestId("total")).ToHaveTextAsync("1.234,50 €")));
```

`TimeZone("Asia/Tokyo")` renders a story in a time zone (IANA ids; Windows ids are converted). Every driver gives the story a `TimeProvider` whose `LocalTimeZone` is that zone (with `UseFakeTime`, the fake clock carries it), so components that compute local time through an injected `TimeProvider` (`Time.GetLocalNow()`) render the same everywhere. Browsers run in that zone too, which JavaScript and WebAssembly's `TimeZoneInfo.Local` follow. Server-side code that reads `DateTime.Now` or `TimeZoneInfo.Local` keeps the host's zone: inject `TimeProvider` instead. An unknown zone fails when the catalog is built.

## Known failures, unfinished and slow stories

* `ExpectFailure("#123: focus is lost after hydration")` marks a known bug, as Playwright's `test.fail()` or pytest's strict `xfail`: the story keeps running, a failure is reported as expected and passes, and a run that passes on its first attempt fails with "expected to fail", so the annotation goes once the bug is fixed. A failure on any attempt counts, so an intermittent known issue does not fail on a retry. `when:` limits it to some runs: `ExpectFailure("#7", run => run.HostingModel == HostingModel.InteractiveWebAssembly)` (the run's `Driver` is there too). Reports show "expected failure" or "unexpectedly passed"; the in-app driver applies it as well.
* `Fixme("needs the new date picker")` skips the story with the reason, prefixed "Fix me", and adds the `fixme` tag, so `BTP_TAGS=fixme` lists them.
* `Slow()` triples the story's timeout (its own `Timeout(...)`, else the runner's), as Playwright's `test.slow()`.

Prefer `ExpectFailure` to `Skip` for bugs: a skipped story never tells you it started working.

## Tags and filtering

`.Tags("smoke", "!visual")` adds `smoke` and removes `visual`. Filter catalogs in tests with `catalog.WithTag("smoke")`, `WithoutTag`, `WithPlay()`, `ForComponent<T>()` or `Matching("inputs-*")`. The [test framework packages](test-frameworks.md) turn tags into test categories, and `StoryImpact` selects the stories a change can affect ([CI](ci.md#test-impact-analysis)).

Runs select stories by tag with a boolean expression, in code or from the environment:

```csharp
var forms = catalog.WithTags("forms && !slow");     // a TagExpression: !/not, &&/and, ||/or/, , (), * wildcards
var thisRun = catalog.ForRun();                     // what BTP_TAGS, BTP_GREP, BTP_GREP_INVERT and BTP_LAST_FAILED select
```

`BTP_TAGS="smoke && !visual" dotnet test` runs only the matching stories: the xUnit, NUnit, MSTest and TUnit packages report the others as skipped with the reason, and the bUnit and browser runners skip them too, so plain `MemberData` tests honor the switch. See [configuration](configuration.md).

## Quality-gate options

```csharp
s.Visual(v => { v.Masks.Add(".timestamp"); v.Threshold = 0.2; v.FullPage = true; })
 .NoVisual()
 .DisableAccessibilityRule("color-contrast", "Brand red approved by design, tracked in #123")
 .Performance(p => { p.MaxRootRenders = 2; p.MaxAllocatedBytes = 50_000; })
 .OnlyHostingModels(HostingModel.InteractiveServer)
 .ExcludeHostingModels(HostingModel.StaticServer)
 .Prerender(false)          // for stories whose first render never completes (pending loads); honoured by every browser run
 .Timeout(TimeSpan.FromSeconds(10))  // whole-run limit; BunitStoryOptions.Timeout and BrowserRunOptions.Timeout override it
 .BrowserBudget(b => { b.MaxLayoutShift = 0.1; b.MaxDomNodes = 400; })   // real-browser runtime metrics
 .Geolocation(52.37, 4.89)  // browser emulation: real browsers only (see interactions)
 .Skip("Waiting for API v2");
```

Options are read-only once a story is built: catalogs are cached and shared by every test, so a test that changed a
story's `Visual`, `Accessibility`, `Performance` or `BrowserBudget` options would change them for all the others. Setting
one throws `InvalidOperationException`; pass run options (`BunitStoryOptions`, `BrowserRunOptions`) instead.

Every story knows where its collection is declared (`Story.SourceFile`, `Story.SourceLine`, also on results): reports,
SARIF logs and impact analysis point at that file. The source generator records it for every collection it discovers.

## Compile-time checks

The analyzers ship with `BlazorTestPlatform.Core`, next to the source generator, so mistakes show up in the editor instead of when the catalog is built or the story renders:

| Id | Severity | Reports |
|---|---|---|
| `BTP001` | Error | `Arg`, `Bind`, `Fragment`, `Template`, `Action` or `Vary` selects a property that isn't a `[Parameter]` |
| `BTP002` | Error | `Bind(c => c.X, ...)` on a component without an `XChanged` parameter of type `EventCallback<T>` (same `T`) |
| `BTP003` | Warning | `Attribute("x", ...)` where `x` is a declared parameter: use `Arg`, which checks the type and enables live controls |
| `BTP004` | Warning | `Attribute(...)` on a component without a `[Parameter(CaptureUnmatchedValues = true)]` property, which fails at render |
| `BTP005` | Error | Two stories in one collection whose names give the same id (`"Primary"` and `"primary!"`) |

Inherited parameters count. Suppress a rule as usual, with `#pragma warning disable BTP003` or `<NoWarn>` in the project, for example in tests that build invalid stories on purpose.

## Composition stories

To render several components together, use the neutral `Composition` host:

```csharp
public sealed class CheckoutStories : Stories<Composition>
{
    protected override void Define(StoryCollectionBuilder<Composition> stories) =>
        stories.Add("Happy path", s => s.ChildContent(b => { /* render several components */ }));
}
```

Keep story collections in `.cs` files: the catalog generator only sees C# sources, and a collection declared in a `.razor` file would be missing from the generated catalog. To write fragments with Razor template syntax, put them in a small `.razor` component and use it from the story:

```razor
@* Fragments.razor, next to the stories *@
@code {
    public static RenderFragment Greeting = @<p>Hello <strong>world</strong></p>;
}
```

```csharp
stories.Add("Rich content", s => s.ChildContent(Fragments.Greeting));
```
