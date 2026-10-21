using BlazorTestPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace Contoso.UI.Stories;

/// <summary>Deterministic, seeded in-memory backend for the todo list.</summary>
public sealed class TodoFixture(int count = 4) : StoryFixture
{
    public TodoFixture()
        : this(4)
    {
    }

    public override void ConfigureServices(IServiceCollection services, FixtureContext context)
    {
        var items = context.Fake.Many(count, (fake, i) => new TodoItem(fake.Id(), fake.Sentence(4).TrimEnd('.'), i % 3 == 0));
        services.AddSingleton<ITodoService>(new InMemoryTodoService(items, context.Random.Fork("ids")));
    }

    private sealed class InMemoryTodoService(IEnumerable<TodoItem> seed, DeterministicRandom random) : ITodoService
    {
        private readonly List<TodoItem> _items = [.. seed];

        public Task<IReadOnlyList<TodoItem>> GetAllAsync() => Task.FromResult<IReadOnlyList<TodoItem>>(_items.ToArray());

        public Task<TodoItem> AddAsync(string title)
        {
            var item = new TodoItem(random.NextGuid(), title, false);
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task<TodoItem> SetDoneAsync(Guid id, bool done)
        {
            var index = _items.FindIndex(i => i.Id == id);
            _items[index] = _items[index] with { Done = done };
            return Task.FromResult(_items[index]);
        }
    }
}

/// <summary>A backend that never answers, to capture the loading state.</summary>
public sealed class PendingTodoFixture : StoryFixture
{
    public override void ConfigureServices(IServiceCollection services, FixtureContext context) =>
        services.AddSingleton<ITodoService>(new PendingTodoService());

    private sealed class PendingTodoService : ITodoService
    {
        private readonly TaskCompletionSource<IReadOnlyList<TodoItem>> _never = new();

        public Task<IReadOnlyList<TodoItem>> GetAllAsync() => _never.Task;

        public Task<TodoItem> AddAsync(string title) => throw new NotSupportedException();

        public Task<TodoItem> SetDoneAsync(Guid id, bool done) => throw new NotSupportedException();
    }
}

public sealed class TodoListStories : Stories<TodoList>
{
    public override string Title => "Data/Todo list";

    protected override void Define(StoryCollectionBuilder<TodoList> stories)
    {
        stories.Defaults(s => s.Arg(t => t.Title, "Sprint tasks"));

        stories.Add("Seeded", s => s.UseFixture<TodoFixture>().Seed(2026));
        stories.Add("Empty", s => s.UseFixture(new TodoFixture(0)));
        stories.Add("Loading", s => s
            .UseFixture<PendingTodoFixture>()
            .Prerender(false)
            .Visual(v => v.WaitForSelector = ".c-todo__loading"));

        stories.Add("Add a task", s => s
            .UseFixture(new TodoFixture(1))
            .Play(async ctx =>
            {
                var input = ctx.GetByLabel("New task");
                var add = ctx.GetByRole("button", "Add");
                await ctx.Expect(add).ToBeDisabledAsync();

                await input.FillAsync("Ship the release");
                await add.ClickAsync();

                await ctx.Expect(ctx.GetByRole("listitem").Filter("Ship the release")).ToBeVisibleAsync();
                await ctx.Expect(ctx.GetByRole("listitem")).ToHaveCountAsync(2);
                await ctx.Expect(input).ToHaveValueAsync(string.Empty);
            }));

        stories.Add("Complete a task", s => s
            .UseFixture(new TodoFixture(2))
            .Play(async ctx =>
            {
                var summary = ctx.GetByText("remaining");
                await ctx.Expect(summary).ToHaveTextAsync("1 of 2 remaining");
                await ctx.GetByRole("checkbox").Nth(1).CheckAsync();
                await ctx.Expect(summary).ToHaveTextAsync("0 of 2 remaining");
            }));
    }
}
