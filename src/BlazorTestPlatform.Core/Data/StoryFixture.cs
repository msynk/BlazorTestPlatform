using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform;

/// <summary>
/// A reusable, deterministic test-data fixture: registers fake/override services and seeds data.
/// Fixtures are attached to stories with <c>UseFixture&lt;T&gt;()</c> and run identically in bUnit,
/// the workbench and browser runs.
/// </summary>
/// <example>
/// <code>
/// public sealed class TodoFixture : StoryFixture
/// {
///     public override void ConfigureServices(IServiceCollection services, FixtureContext context)
///         => services.AddSingleton&lt;ITodoService&gt;(new InMemoryTodoService(context.Fake.Many(5, (f, i) => new Todo(f.Sentence(3)))));
/// }
/// </code>
/// </example>
public abstract class StoryFixture
{
    /// <summary>Registers services. Registrations here override the application's own registrations.</summary>
    public virtual void ConfigureServices(IServiceCollection services, FixtureContext context)
    {
    }

    /// <summary>Seeds data once the service provider is built (before the story renders).</summary>
    public virtual Task InitializeAsync(IServiceProvider services, FixtureContext context) => Task.CompletedTask;
}

/// <summary>Deterministic context handed to fixtures and service configurators.</summary>
public sealed class FixtureContext
{
    /// <summary>Creates a fixture context.</summary>
    public FixtureContext(Story story, HostingModel hostingModel)
    {
        Story = story ?? throw new ArgumentNullException(nameof(story));
        HostingModel = hostingModel;
        Random = new DeterministicRandom(story.Seed).Fork("fixture");
        Fake = new FakeData(Random.Fork("fake"));
    }

    /// <summary>The story being prepared.</summary>
    public Story Story { get; }

    /// <summary>The hosting model the story is rendered with.</summary>
    public HostingModel HostingModel { get; }

    /// <summary>Seeded random source (derived from <see cref="Story.Seed"/>).</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Seeded fake-data generator.</summary>
    public FakeData Fake { get; }

    /// <summary>The frozen clock start, when the story uses fake time.</summary>
    public DateTimeOffset? FrozenTime => Story.FakeTimeStart;
}
