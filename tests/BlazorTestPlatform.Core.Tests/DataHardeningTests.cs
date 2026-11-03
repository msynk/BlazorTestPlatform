using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BlazorTestPlatform.Core.Tests;

/// <summary>Regression tests for test-data, HTTP-mock, JS-mock and matrix defects found by the audit.</summary>
public sealed class DataHardeningTests
{
    [Fact]
    public async Task Clearing_requests_while_one_is_in_flight_neither_throws_nor_reuses_sequence_numbers()
    {
        var time = new FakeTimeProvider();
        var mock = new HttpMock { Time = time };
        mock.Get("api/slow").Delay(TimeSpan.FromSeconds(1)).RespondText("late");
        mock.Get("api/fast").RespondText("now");
        using var client = mock.CreateClient();

        var slow = client.GetAsync("api/slow", TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        mock.ClearRequests();
        using (await client.GetAsync("api/fast", TestContext.Current.CancellationToken))
        {
        }

        time.Advance(TimeSpan.FromSeconds(1));
        using var response = await slow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fast = Assert.Single(mock.Requests);
        Assert.Equal(2, fast.Sequence);
        Assert.Equal(200, fast.StatusCode);
    }

    [Theory]
    [InlineData("https://api.contoso.com/v1", "people", "people")]
    [InlineData("https://api.contoso.com/v1/", "people", "people")]
    [InlineData("https://api.contoso.com/v1", "v10/x", "v10/x")]
    public async Task Relative_urls_follow_how_http_client_resolves_the_base_address(string baseAddress, string request, string rule)
    {
        var mock = new HttpMock { BaseAddress = new Uri(baseAddress) };
        mock.Get(rule).RespondText("matched");
        using var client = mock.CreateClient();

        Assert.Equal("matched", await client.GetStringAsync(request, TestContext.Current.CancellationToken));
    }

    private sealed class StampHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Stamp", "app-handler");
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Mocked_named_clients_keep_the_application_configuration()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("people", c =>
        {
            c.BaseAddress = new Uri("https://api.contoso.com/v2/");
            c.DefaultRequestHeaders.Add("X-App", "contoso");
        }).AddHttpMessageHandler(() => new StampHandler());
        var mock = new HttpMock();
        mock.Get("people").RespondText("[]");
        using var provider = services.BuildServiceProvider();

        using var client = new HttpMockClientFactory(mock, provider).CreateClient("people");
        Assert.Equal("[]", await client.GetStringAsync("people", TestContext.Current.CancellationToken));

        var request = Assert.Single(mock.Requests);
        Assert.Equal("https://api.contoso.com/v2/people", request.Url);
        Assert.Equal("people", request.RelativeUrl);
        Assert.Equal("contoso", request.Headers["X-App"]);
        Assert.Equal("app-handler", request.Headers["X-Stamp"]);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void Mock_clients_never_time_out_on_their_own()
    {
        using var client = new HttpMock().CreateClient();

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void Module_setups_accumulate_across_collection_defaults_and_the_story()
    {
        var defaults = new JSInteropMocks().Module("./chart.js", m => m.SetupVoid("init"));
        var story = new JSInteropMocks().Module("./chart.js", m =>
        {
            m.Setup("size", 3);
            m.Strict = true;
        });
        var merged = new JSInteropMocks();
        merged.MergeFrom(defaults);
        merged.MergeFrom(story);

        var chart = merged.FindModule("./chart.js")!;
        Assert.Single(merged.Modules);
        Assert.NotNull(chart.Find("init", [], resultType: null));
        Assert.Equal(3, chart.Find("size", [], typeof(int))!.BoxedResult);
        Assert.True(chart.Strict);

        // Merging copied the setups: the collection's own mocks are unchanged.
        Assert.Null(defaults.FindModule("./chart.js")!.Find("size", [], typeof(int)));
    }

    [Fact]
    public void Matrix_dimensions_are_deduplicated_and_in_memory_keys_rejected()
    {
        var catalog = StoryCatalog.FromAssemblyOf<DataHardeningTests>();
        var matrix = TestMatrix.Default().WithViewports(Viewport.Desktop).WithBrowsers(Browsers.Chromium);

        var keys = matrix.Expand(catalog.Where(s => s.Id == "samples-greeter--default")).Select(c => c.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(2, keys.Length);
        Assert.Throws<FormatException>(() => matrix.Resolve(catalog, "samples-greeter--default|desktop|light|memory|chromium"));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-10, -5)]
    [InlineData(0, 1)]
    public void Next_decimal_stays_in_its_half_open_range(int min, int max)
    {
        var random = new DeterministicRandom(7);
        for (var i = 0; i < 20_000; i++)
        {
            var value = random.NextDecimal(min, max);
            Assert.InRange(value, min, max - 0.01m);
        }
    }

    [Fact]
    public void Next_decimal_keeps_the_sequence_for_non_negative_ranges()
    {
        // Same draw, same truncation as earlier versions for ranges at or above zero.
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (var i = 0; i < 100; i++)
        {
            var expected = Math.Round(5m + (95m * (decimal)b.NextDouble()), 2, MidpointRounding.ToZero);
            Assert.Equal(expected, a.NextDecimal(5, 100));
        }
    }

    [Fact]
    public void Time_values_with_sub_second_precision_round_trip()
    {
        var time = new TimeOnly(10, 0, 0).Add(TimeSpan.FromTicks(5_000));

        var text = StoryArgs.Format(time);

        Assert.NotEqual("10:00", text);
        Assert.True(StoryArgs.TryConvert(text, typeof(TimeOnly), out var parsed, out _));
        Assert.Equal(time, parsed);
        Assert.Equal("10:00", StoryArgs.Format(new TimeOnly(10, 0)));
    }

    public enum Kind
    {
        Primary = 0,
        Default = 0,
        Danger = 1,
    }

    public sealed class Aliased : ComponentBase
    {
        [Parameter] public Kind Kind { get; set; }
    }

    private sealed class AliasedStories : Stories<Aliased>
    {
        public override string Title => "Tests/Aliased";

        protected override void Define(StoryCollectionBuilder<Aliased> stories)
        {
            stories.Add("Primary");
            stories.Add("Danger", s => s.Arg(a => a.Kind, Kind.Danger));
        }
    }

    [Fact]
    public void Enum_aliases_are_one_state()
    {
        var story = StoryCatalog.FromCollections([new AliasedStories()]).Get("tests-aliased--primary");
        var control = StoryArgs.ControlsFor(story).Single(c => c.Name == "Kind");

        Assert.Equal(2, control.Options.Count);
        Assert.Contains(StoryArgs.Format(Kind.Default), control.Options);
    }
}
