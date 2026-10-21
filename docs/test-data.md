# Test data

Screenshots, accessibility fingerprints and assertions must not change between runs. Every source of variation therefore comes from one seed per story.

## Seeds

* `story.Seed` defaults to a stable hash of the story id. Set it explicitly with `.Seed(42)`.
* `DeterministicRandom` (xoshiro256**) produces the same sequence on every OS and runtime. The sequence is pinned by tests and is part of the public contract.
* `Fork("purpose")` derives independent streams. Adding a `Fork("orders")` call later does not change the values produced by `Fork("users")`.

## Fake data

```csharp
s.Arg(t => t.Items, ctx => ctx.Fake.Many(5, (f, i) => new Person(f.FullName(), f.Email())));
```

`FakeData` provides names, user names, e-mails on reserved domains (`example.com`, `example.org`, `example.net` and `mail.example`), companies, cities, phone numbers in the 555-0100 to 555-0199 block reserved for fiction, lorem text, prices, dates and GUIDs. To use a richer library, seed it with `ctx.Random.Seed`.

## Fixtures

A fixture registers fakes and seeds data. The same fixture works in bUnit and in browsers:

```csharp
public sealed class TodoFixture(int count) : StoryFixture
{
    public override void ConfigureServices(IServiceCollection services, FixtureContext context)
    {
        var items = context.Fake.Many(count, (f, i) => new TodoItem(f.Id(), f.Sentence(4), i % 3 == 0));
        services.AddSingleton<ITodoService>(new InMemoryTodoService(items));
    }

    public override Task InitializeAsync(IServiceProvider services, FixtureContext context) => Task.CompletedTask;
}

stories.Add("Seeded", s => s.UseFixture(new TodoFixture(4)));
```

For one-off overrides, use `.Services(services => services.AddSingleton<IClock>(...))`.

### How overrides reach Server and WebAssembly

`AddBlazorTestWorkbench` must be called **after** the application's own registrations. It probes every story for the service types it overrides and replaces those registrations with a resolver that does the following:

1. Use the active story's override, if it has one. Overrides are built per frame, and every frame is a fresh circuit, WebAssembly app or SSR request.
2. Otherwise use the application's original registration, kept as a keyed service with its original lifetime.

Services nobody overrides are untouched. Overrides of services that singletons consume fall back to the real service, because there is no story scope at the root.

Services that the application registers more than once, or that are resolved as collections, are **composed** rather than replaced, exactly as if the story had called `services.Add...` after the application:

| Resolution | What the story frame gets |
|---|---|
| `IEnumerable<T>` / `GetServices<T>()` | The application's registrations followed by the story's. Nothing is collapsed. |
| `T` / `GetService<T>()` | The story's last registration, or the application's last when the story adds none. |

This covers your own multi-registered services (notification channels, plug-ins...), authorization handlers (`AuthorizeView` and policies see the story's handlers) and options. For options, `IOptions<T>` and `IOptionsMonitor<T>` resolve per story for every options type a story configures, so a story's `services.Configure<T>(...)` reaches components under Server and WebAssembly. Application singletons keep the application's values, because they're built once at the root.

A few contracts are consumed once when the application or a circuit starts, before any story frame exists: `IHostedService`, `ILoggerProvider`, `IStartupFilter`, `CircuitHandler` and `IHttpMessageHandlerBuilderFilter`. Story registrations of these apply in bUnit only. The same goes for `IJSRuntime` when a story uses `MockJS` but the application registers no JS runtime to decorate. The workbench flags affected stories: a notice in the shell, a `data-btp-unsupported-overrides` attribute on the frame, and a warning in the browser run's timeline. `WorkbenchOptions.UnwrappableOverrides` lists them, and `UnsupportedOverridesOf(story)` returns a story's.

## Mocked HTTP

Most real components fetch data. `MockHttp` gives each story its own fake backend. It answers requests made through
`HttpClient` and through `IHttpClientFactory`, which covers named and typed clients. It works the same in bUnit, under Interactive Server (where the mock runs on the server) and under WebAssembly (where it runs in the browser):

```csharp
stories.Defaults(s => s
    .Services(services => services.AddTransient<PeopleClient>())       // the typed client under test
    .MockHttp(http =>
    {
        http.Get("api/people").RespondJson(Everyone, ContosoJsonContext.Default.PersonArray);
        http.Get("api/people?team=Design").RespondJson(Designers, ContosoJsonContext.Default.PersonArray);
    }));

stories.Add("Server error with retry", s => s
    .MockHttp(http => http.Get("api/people").RespondStatus(500).Times(1))   // later rules win; Times falls back
    .Play(async ctx =>
    {
        await ctx.GetByRole("button", "Retry").ClickAsync();
        await ctx.ExpectRequest("GET", "api/people").ToHaveBeenMadeAsync(times: 2);
    }));

stories.Add("Loading", s => s.MockHttp(http => http.Get("api/people").Never()).Prerender(false));
```

| Rule | Meaning |
|---|---|
| `Get/Post/Put/Patch/Delete(pattern)`, `Any(pattern)`, `Match(predicate)` | Patterns are relative to `BaseAddress` (default `https://localhost/`) or absolute. `*` is a wildcard, and the query string matters only if the pattern contains `?`. |
| `RespondJson(value, typeInfo)`, `RespondJsonText`, `RespondText`, `RespondStatus`, `Respond(request => response)` | Response. The `JsonTypeInfo` overload is safe under trimming. |
| `Delay(t)` | Delays the response on the story's clock. With fake time the response stays pending until the play script advances the clock past the delay (`ctx.AdvanceTimeAsync`), so loading states are deterministic. |
| `Never()`, `Fail()` | A request that never completes (loading states), or a network failure. |
| `Times(n)` | Answers the next *n* requests, then lets earlier rules answer. |

Unmatched requests get a `404` and are still recorded; set `Unmatched = UnmatchedRequestBehavior.Throw` to fail them as network errors instead. Requests appear in the HTML report for bUnit runs and interactive browser runs (static SSR frames have no frame bridge to read them from). Assert on requests in any driver with `ctx.ExpectRequest(method, pattern)`.

To route static assets or third-party scripts (not application API calls), use `BrowserRunOptions.ConfigurePageAsync` and `page.RouteAsync`.

## JavaScript-interop mocks

bUnit has no JavaScript engine, so `MockJS` always answers there. In browsers the real JavaScript runs by default. Call `ApplyInBrowsers()` to answer the mocked identifiers under Server, WebAssembly and Auto as well, for browser APIs or third-party scripts whose real results would make the story non-deterministic:

```csharp
s.MockJS(js => js
    .ApplyInBrowsers()                                            // also answer in workbench frames
    .Setup<string?>("localStorage.getItem", null, "consent")      // exact arguments
    .SetupVoid("analytics.track")                                 // any arguments
    .Module("./media.js", media => media.Setup("prefersReducedMotion", true)));
s.MockJS(js => js.Strict = true);                                 // fail on calls without a setup
```

In browsers, the workbench decorates the `IJSRuntime` that the story's components inject. With `ApplyInBrowsers()`, mocked identifiers answer from the setups and every other call reaches the real JavaScript; in strict mode, every other call fails instead. Without `ApplyInBrowsers()` the decorator only records calls, so `Strict` has no effect in browsers. Blazor's own `Blazor.*` calls and the workbench's internal calls are never intercepted. A mocked module doesn't have to exist in the browser. If its import fails, unmocked calls on it return defaults, as in bUnit's loose mode. Later setups win, as with `MockHttp`. The two switches are additive: once any `MockJS` call on a story (including its collection and assembly defaults) turns `ApplyInBrowsers` or `Strict` on, a later call can't turn it off.

A story that declares `MockJS` (even an empty `MockJS(_ => { })`) has its JS calls recorded under every driver. Assert on them in play functions:

```csharp
await ctx.ExpectJSCall("localStorage.setItem").ToHaveBeenLastCalledWithAsync("consent", "accepted");
await ctx.ExpectJSCall("prefersReducedMotion", module: "./media.js").ToHaveBeenCalledAsync(times: 1);
await ctx.ExpectJSCall("analytics.track").NotToHaveBeenCalledAsync();
var calls = await ctx.GetJSCallsAsync();                          // identifier, module, arguments, mocked
```

Arguments are compared after formatting: strings as-is, numbers and dates in the invariant culture, and booleans as `true`/`false`. See `Feedback/Cookie consent` in the sample for a story that reads and writes `localStorage` deterministically in every render mode.

## Fake users

```csharp
s.Anonymous();
s.AuthenticatedAs("Ada Lovelace", "Admin");
s.User(TestUser.Named("Grace").WithRoles("Admin").WithClaim("email", "grace@example.com"));
s.User(TestUser.Authenticating);      // AuthorizeView shows <Authorizing>; combine with .Prerender(false)
```

The user is provided as a cascading `Task<AuthenticationState>` (which `AuthorizeView` and `[CascadingParameter]` consume) and as an `AuthenticationStateProvider` service. Roles and policies are evaluated by the real authorization services. To test sign-in transitions, call `FakeAuthenticationStateProvider.SetUser(...)` during play (in-memory only).

## Fake time

`.UseFakeTime(start)` registers a `FakeTimeProvider` as `TimeProvider`. Advance it with `ctx.AdvanceTimeAsync(...)`. See [interactions](interactions.md#fake-time).
