using Microsoft.Extensions.DependencyInjection;

namespace Contoso.UI;

public sealed record TodoItem(Guid Id, string Title, bool Done);

public interface ITodoService
{
    Task<IReadOnlyList<TodoItem>> GetAllAsync();

    Task<TodoItem> AddAsync(string title);

    Task<TodoItem> SetDoneAsync(Guid id, bool done);
}

/// <summary>The "real" service used by the app (simulates a slow backend).</summary>
public sealed class LocalTodoService(TimeProvider time) : ITodoService
{
    private readonly List<TodoItem> _items =
    [
        new(Guid.Parse("8f1c3f7e-1d2b-4c51-9a77-0d6e2a3b4c5d"), "Write stories for every component", true),
        new(Guid.Parse("2b7e4a91-6c3d-4f20-8e15-7a9b0c1d2e3f"), "Run the cross-hosting matrix", false),
    ];

    public async Task<IReadOnlyList<TodoItem>> GetAllAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), time);
        return _items.ToArray();
    }

    public Task<TodoItem> AddAsync(string title)
    {
        var item = new TodoItem(Guid.NewGuid(), title, false);
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

public static class ContosoServiceCollectionExtensions
{
    /// <summary>Registers the services Contoso.UI components depend on.</summary>
    public static IServiceCollection AddContosoUI(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITodoService, LocalTodoService>();
        services.AddHttpClient<PeopleClient>(client => client.BaseAddress = new Uri("https://api.contoso.example/"));
        return services;
    }
}
