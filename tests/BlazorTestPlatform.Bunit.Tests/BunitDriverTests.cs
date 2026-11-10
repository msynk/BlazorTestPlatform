using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class BunitDriverTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<BunitDriverTests>();

    private static readonly BtpSettings Settings = BtpSettings.Load(
        Path.Combine(Path.GetTempPath(), "btp-bunit-tests"),
        new Dictionary<string, string?>());

    private static BunitStoryOptions Options => new() { Settings = Settings, WriteResults = false };

    [Fact]
    public async Task Click_updates_markup_and_records_auto_action()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-counter--default"), Options);
        var ctx = session.StoryContext;

        await ctx.GetByRole("button", "Increment").ClickAsync();

        await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 1");
        await ctx.ExpectAction("OnCountChanged").ToHaveBeenCalledAsync(times: 1);
        Assert.Equal(1, session.Actions.For("OnCountChanged")[0].ArgumentAs<int>());
    }

    [Fact]
    public async Task Actions_do_not_wait_for_handlers_that_wait_for_fake_time()
    {
        // As in a browser, the click returns once the handler yields; waiting for it would deadlock on the fake clock.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-delayed-save--default"), Options, cancellationToken);
        var ctx = session.StoryContext;

        await ctx.GetByRole("button", "Save").ClickAsync().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Saving…");
        await ctx.ExpectAction("OnSaved").NotToHaveBeenCalledAsync();

        await ctx.AdvanceTimeAsync(TimeSpan.FromSeconds(5));

        await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Saved");
        await ctx.ExpectAction("OnSaved").ToHaveBeenCalledAsync(times: 1);
    }

    [Fact]
    public async Task Quarantined_failures_are_recorded_but_do_not_fail_verify()
    {
        var directory = Directory.CreateTempSubdirectory("btp-quarantined-").FullName;
        try
        {
            QuarantineList.From([new QuarantineEntry { Pattern = "tests-inaccessible--*", Reason = "Known a11y debt, #321" }])
                .Save(Path.Combine(directory, QuarantineList.DefaultFileName));
            var settings = BtpSettings.Load(directory, new Dictionary<string, string?>());
            var options = new BunitStoryOptions { Settings = settings, WriteResults = false };

            var result = await BunitStoryRunner.VerifyAsync(Catalog.Get("tests-inaccessible--default"), options, TestContext.Current.CancellationToken);

            Assert.Equal(StoryOutcome.Failed, result.Outcome);
            Assert.Equal("Known a11y debt, #321", result.Quarantine?.Reason);
            Assert.False(result.IsBlockingFailure);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Args_set_component_parameters()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-counter--by-five"), Options);
        await session.StoryContext.GetByText("Increment").ClickAsync();
        await session.StoryContext.Expect(By.Text("Current count: 5")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Play_function_passes_and_result_is_recorded()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--clicks-twice"), Options);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        Assert.Contains(result.Steps, s => s.Title == "play");
        Assert.NotNull(result.Performance);
        Assert.True(result.Performance!.RootRenderCount >= 3, result.Performance.ToString());
    }

    [Fact]
    public async Task Failing_play_reports_timeline_and_actual_value()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--failing-play"), Options);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("Current count: 0", result.Error, StringComparison.Ordinal);
        var play = Assert.Single(result.Steps, s => s.Title == "play");
        Assert.True(play.Failed);
        Assert.Equal("check wrong text", Assert.Single(play.Children).Title);
        await Assert.ThrowsAsync<StoryFailedException>(() => BunitStoryRunner.VerifyAsync(Catalog.Get("tests-counter--failing-play"), Options));
    }

    [Fact]
    public async Task Render_budget_violation_fails_the_story()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--over-budget"), Options);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("Root renders", result.Error, StringComparison.Ordinal);
        Assert.NotEmpty(result.BudgetViolations);
    }

    [Fact]
    public async Task Missing_capability_skips_instead_of_failing()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--needs-layout"), Options);

        Assert.Equal(StoryOutcome.Skipped, result.Outcome);
        Assert.Contains("Layout", result.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exceptions_thrown_after_an_await_fail_the_story()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-crashes-later--failing-after-await"), Options);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("boom after await", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--clicks-twice"), Options, cancelled.Token));
    }

    [Fact]
    public async Task Story_timeouts_are_reported_as_failures()
    {
        var options = Options;
        options.Timeout = TimeSpan.FromMilliseconds(1);

        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--failing-play"), options);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("timed out", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_with_another_viewport_get_their_own_result_key()
    {
        var mobile = Options;
        mobile.Viewport = Viewport.Mobile;

        var desktopResult = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--default"), Options);
        var mobileResult = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--default"), mobile);

        Assert.Equal("tests-counter--default|bunit", desktopResult.Key);
        Assert.Equal("tests-counter--default|bunit|mobile", mobileResult.Key);
    }

    [Fact]
    public void Medians_average_the_middle_pair()
    {
        Assert.Equal(2.5, BunitStoryRunner.Median([1, 2, 3, 4]));
        Assert.Equal(3, BunitStoryRunner.Median([1, 3, 9]));
    }

    [Fact]
    public async Task Declared_requirements_skip_before_rendering_with_the_reason()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-counter--declares-layout"), Options);

        Assert.Equal(StoryOutcome.Skipped, result.Outcome);
        Assert.Contains("Layout (measuring the popover position)", result.SkipReason, StringComparison.Ordinal);
        Assert.Contains("'bunit'", result.SkipReason, StringComparison.Ordinal);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task Declared_requirements_the_driver_meets_run_normally()
    {
        var story = Catalog.Get("tests-counter--declares-fake-time");
        var result = await BunitStoryRunner.RunAsync(story, Options);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        Assert.True(story.CanRunOn(BunitStoryDriver.CapabilitiesFor(story)));
    }

    [Fact]
    public void Catalogs_select_the_stories_a_driver_can_run()
    {
        var inMemory = Catalog.Supporting(DriverCapabilities.None);
        var browser = Catalog.Supporting(DriverCapabilities.RealBrowser | DriverCapabilities.Layout | DriverCapabilities.FakeTime);

        Assert.DoesNotContain("tests-counter--declares-layout", inMemory.Ids);
        Assert.DoesNotContain("tests-counter--declares-fake-time", inMemory.Ids);
        Assert.Contains("tests-counter--declares-layout", browser.Ids);
        Assert.Contains("tests-counter--declares-fake-time", browser.Ids);
        Assert.Equal(DriverCapabilities.Layout, Catalog.Get("tests-counter--declares-layout").RequiredCapabilities);
    }

    [Fact]
    public async Task Fill_commits_change_on_blur_and_submit_button_submits_once()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-name-form--default"), Options);
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Name").FillAsync("Ada");
        await ctx.GetByLabel("Subscribe").CheckAsync();
        await ctx.GetByRole("button", "Save").ClickAsync();

        await ctx.Expect(ctx.GetByTestId("result")).ToHaveTextAsync("Saved Ada (subscribe: True)");
        await ctx.Expect(ctx.GetByTestId("submits")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Enter_in_text_input_submits_the_form()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-name-form--default"), Options);
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Name").TypeAsync("Grace");
        await ctx.PressAsync(Keys.Enter);

        await ctx.Expect(ctx.GetByTestId("result")).ToHaveTextAsync("Saved Grace (subscribe: False)");
    }

    [Fact]
    public async Task Clicking_a_label_toggles_its_checkbox()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-name-form--default"), Options);
        var ctx = session.StoryContext;

        await ctx.GetByText("Subscribe").ClickAsync();

        await ctx.Expect(ctx.GetByLabel("Subscribe")).ToBeCheckedAsync();
    }

    [Fact]
    public async Task Tab_follows_the_tab_order()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-name-form--default"), Options);
        var ctx = session.StoryContext;

        await ctx.ExpectTabOrderAsync(ctx.GetByLabel("Name"), ctx.GetByLabel("Subscribe"), ctx.GetByRole("button", "Save"));
        await ctx.ShiftTabAsync();
        await ctx.Expect(ctx.GetByLabel("Subscribe")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Roving_tabindex_and_programmatic_focus_are_honoured()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-tabs--default"), Options);
        var ctx = session.StoryContext;

        await ctx.TabAsync();
        await ctx.Expect(ctx.GetByRole("tab", "One")).ToBeFocusedAsync();

        await ctx.PressAsync(Keys.ArrowRight);
        await ctx.Expect(ctx.GetByRole("tab", "Two")).ToBeFocusedAsync();
        await ctx.Expect(ctx.GetByRole("tab", "Two")).ToHaveAttributeAsync("aria-selected", "true");
        await ctx.Expect(ctx.GetByRole("tabpanel")).ToHaveTextAsync("Panel Two");

        // Only the selected tab is in the tab sequence: Tab leaves the tablist.
        await ctx.TabAsync();
        await ctx.Expect(ctx.GetByRole("link", "After tabs")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Fake_time_controls_async_work()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-async-loader--frozen-clock"), Options);
        var ctx = session.StoryContext;

        await ctx.Expect(By.Text("Loading")).ToBeVisibleAsync();
        await ctx.AdvanceTimeAsync(TimeSpan.FromSeconds(5));
        await ctx.Expect(By.Text("Loaded 2030-01-02")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("tests-greeting--anonymous", "Please sign in.", false)]
    [InlineData("tests-greeting--signed-in", "Hello, Ada!", false)]
    [InlineData("tests-greeting--admin", "Hello, Grace!", true)]
    public async Task Fake_users_drive_authorize_view(string storyId, string expected, bool admin)
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get(storyId), Options);
        var ctx = session.StoryContext;

        await ctx.Expect(By.Text(expected, exact: true)).ToBeVisibleAsync();
        await ctx.Expect(By.Text("Admin tools")).ToHaveCountAsync(admin ? 1 : 0);
    }

    [Fact]
    public async Task Fixtures_override_services_with_seeded_data()
    {
        await using var first = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-weather--seeded"), Options);
        await using var second = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-weather--seeded"), Options);

        await first.StoryContext.Expect(first.StoryContext.GetByRole("listitem")).ToHaveCountAsync(3);
        Assert.Equal(first.Markup, second.Markup);
    }

    [Fact]
    public async Task Bind_supplies_two_way_binding()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-rating--bound"), Options);
        var ctx = session.StoryContext;

        await ctx.Expect(By.Css("output")).ToHaveTextAsync("2 of 5");
        await ctx.GetByRole("radio", "4 stars").ClickAsync();

        await ctx.Expect(By.Css("output")).ToHaveTextAsync("4 of 5");
        await ctx.Expect(ctx.GetByRole("radio", "4 stars")).ToBeCheckedAsync();
        await ctx.ExpectAction("ValueChanged").ToHaveBeenLastCalledWithAsync(4);
    }

    [Fact]
    public void Variants_expand_into_one_story_per_combination()
    {
        var variants = Catalog.Stories.Where(s => s.Title == "Tests/Rating" && s.Name.StartsWith("Value=", StringComparison.Ordinal)).ToArray();

        Assert.Equal(4, variants.Length);
        Assert.Contains(variants, s => s.Id == "tests-rating--value-3-max-5");
    }

    [Fact]
    public async Task Static_accessibility_rules_fail_inaccessible_markup()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-inaccessible--default"), Options);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("image-alt", result.AccessibilityFailures);
        Assert.Contains("button-name", result.AccessibilityFailures);
        Assert.Contains("label", result.AccessibilityFailures);
        Assert.Contains("aria-roles", result.AccessibilityFailures);
        Assert.Contains("list", result.AccessibilityFailures);
    }

    [Fact]
    public async Task Justified_rule_suppressions_pass()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-inaccessible--suppressed"), Options);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task Strict_mode_violation_explains_matches()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-tabs--default"), Options);
        var ctx = session.StoryContext;

        var error = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByRole("tab").ClickAsync(timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Contains("strict mode violation: 3 elements", error.Message, StringComparison.Ordinal);
        await ctx.GetByRole("tab").Nth(2).ClickAsync();
        await ctx.Expect(ctx.GetByRole("tab").Last).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task All_well_formed_stories_pass()
    {
        var failures = new List<string>();
        foreach (var story in Catalog.Stories.Where(s => s.Title != "Tests/Inaccessible" || s.Name == "Suppressed").Where(s => !s.Name.Contains("budget", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Failing", StringComparison.Ordinal) && !s.Name.StartsWith("Strict", StringComparison.Ordinal)))
        {
            var result = await BunitStoryRunner.RunAsync(story, Options);
            if (result.Outcome == StoryOutcome.Failed)
            {
                failures.Add($"{story.Id}: {result.Error}");
            }
        }

        Assert.Empty(failures);
    }
}
