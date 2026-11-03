namespace BlazorTestPlatform.Accessibility.Tests;

public sealed class AriaSnapshotTests
{
    [Fact]
    public void Roles_names_and_states_form_an_indented_tree_without_generic_containers()
    {
        var snapshot = AriaSnapshot.Of("""
            <div class="card">
              <nav aria-label="Primary"><ul><li><a href="/">Home</a></li></ul></nav>
              <h3 aria-level="4">Settings</h3>
              <div><button aria-expanded="false">More</button><button disabled>Save "draft"</button></div>
              <label><input type="checkbox" checked> Notify me</label>
              <p>Plain <span>text</span></p>
            </div>
            """);

        Assert.Equal("""
            - navigation "Primary":
              - list:
                - listitem:
                  - link "Home"
            - heading "Settings" [level=4]
            - button "More" [expanded=false]
            - button "Save \"draft\"" [disabled]
            - checkbox "Notify me" [checked]
            - paragraph
            """.Replace("\r\n", "\n", StringComparison.Ordinal), snapshot);
    }

    [Fact]
    public void Hidden_content_and_non_rendered_elements_are_left_out()
    {
        var snapshot = AriaSnapshot.Of("""
            <button hidden>Hidden</button>
            <div aria-hidden="true"><button>Decorative</button></div>
            <div style="display: none"><button>Collapsed</button></div>
            <template><button>Template</button></template>
            <script>var x = 1;</script>
            <button>Shown</button>
            """);

        Assert.Equal("- button \"Shown\"", snapshot);
    }

    [Fact]
    public void Headings_default_to_level_two_and_names_escape_backslashes()
    {
        var snapshot = AriaSnapshot.Of("""<div role="heading">Path C:\temp</div><h5 aria-level="x">Fifth</h5>""");

        Assert.Equal("- heading \"Path C:\\\\temp\" [level=2]\n- heading \"Fifth\" [level=5]", snapshot);
    }

    [Fact]
    public void Text_in_separate_cells_and_blocks_is_set_apart_in_names()
    {
        var snapshot = AriaSnapshot.Of("""<table><tr><th scope="row">Plan</th><td>Team</td></tr></table><button><div>Pay</div>now</button>""");

        Assert.Equal("""
            - table:
              - rowgroup:
                - row "Plan Team":
                  - rowheader "Plan"
                  - cell "Team"
            - button "Pay now"
            """.Replace("\r\n", "\n", StringComparison.Ordinal), snapshot);
    }

    [Fact]
    public void Hand_written_snapshots_are_normalized()
    {
        Assert.Equal("- list:\n  - listitem", AriaSnapshot.Normalize("\r\n    - list:   \r\n\r\n      - listitem\r\n  "));
    }
}
