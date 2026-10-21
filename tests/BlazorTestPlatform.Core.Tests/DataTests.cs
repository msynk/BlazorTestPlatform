using System.Security.Claims;

namespace BlazorTestPlatform.Core.Tests;

public sealed class DataTests
{
    [Fact]
    public void Deterministic_random_is_reproducible()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);

        Assert.Equal(Enumerable.Range(0, 20).Select(_ => a.NextUInt64()), Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));
    }

    [Fact]
    public void Deterministic_random_sequence_is_part_of_the_contract()
    {
        // Pinned values: changing the algorithm would silently change every seeded fixture and screenshot.
        var random = new DeterministicRandom(42);
        Assert.Equal(PinnedSequence, Enumerable.Range(0, 5).Select(_ => random.Next(1000)).ToArray());
    }

    private static readonly int[] PinnedSequence = [83, 378, 680, 924, 991];

    [Fact]
    public void Bounded_values_are_uniform()
    {
        var random = new DeterministicRandom(2024);
        var buckets = new int[10];
        for (var i = 0; i < 100_000; i++)
        {
            buckets[random.Next(10)]++;
        }

        Assert.All(buckets, count => Assert.InRange(count, 9_500, 10_500));
    }

    [Fact]
    public void Forks_are_independent_of_call_order()
    {
        var first = new DeterministicRandom(7);
        first.NextUInt64();
        var forkAfterUse = first.Fork("users").NextUInt64();

        var forkFresh = new DeterministicRandom(7).Fork("users").NextUInt64();

        Assert.Equal(forkFresh, forkAfterUse);
        Assert.NotEqual(new DeterministicRandom(7).Fork("orders").NextUInt64(), forkFresh);
    }

    [Fact]
    public void Bounded_values_stay_in_range()
    {
        var random = new DeterministicRandom(1);
        for (var i = 0; i < 10_000; i++)
        {
            var value = random.Next(-5, 5);
            Assert.InRange(value, -5, 4);
            Assert.InRange(random.NextDouble(), 0, 0.9999999999);
        }
    }

    [Fact]
    public void Guids_are_deterministic_version_4()
    {
        var guid = new DeterministicRandom(3).NextGuid();

        Assert.Equal(guid, new DeterministicRandom(3).NextGuid());
        Assert.Equal('4', guid.ToString()[14]);
    }

    [Fact]
    public void Fake_data_is_seeded()
    {
        var a = new FakeData(99);
        var b = new FakeData(99);

        Assert.Equal(a.FullName(), b.FullName());
        Assert.Equal(a.Email(), b.Email());
        Assert.EndsWith(".", a.Sentence(), StringComparison.Ordinal);
        Assert.All(Enumerable.Range(0, 50).Select(seed => new FakeData(seed).Email()), email => Assert.Matches("^[a-z.]+[0-9]{2}@(example\\.(com|org|net)|mail\\.example)$", email));
        Assert.All(Enumerable.Range(0, 50).Select(seed => new FakeData(seed).Phone()), phone => Assert.Matches("^\\+1-[2-9][0-9]{2}-555-01[0-9]{2}$", phone));
        Assert.Equal(3, a.Many(3, (f, i) => f.Word()).Count);
    }

    [Fact]
    public void Test_users_build_principals()
    {
        var principal = TestUser.Named("Ada").WithRoles("Admin").WithClaim("email", "ada@example.com").ToClaimsPrincipal();

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("Ada", principal.Identity.Name);
        Assert.True(principal.IsInRole("Admin"));
        Assert.Equal("ada@example.com", principal.FindFirst("email")!.Value);
        Assert.False(TestUser.Anonymous.ToClaimsPrincipal().Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Fake_authentication_switches_users_and_supports_pending_state()
    {
        var provider = new FakeAuthenticationStateProvider(TestUser.Authenticating);
        var pending = provider.GetAuthenticationStateAsync();
        Assert.False(pending.IsCompleted);

        Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? notified = null;
        provider.AuthenticationStateChanged += task => notified = task;
        provider.SetUser(TestUser.Named("Grace"));

        Assert.Equal("Grace", (await pending).User.Identity!.Name);
        Assert.Equal("Grace", (await notified!).User.FindFirst(ClaimTypes.Name)?.Value);
    }
}
