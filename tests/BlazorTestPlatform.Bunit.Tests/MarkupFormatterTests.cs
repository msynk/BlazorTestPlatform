using AngleSharp.Html.Parser;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class MarkupFormatterTests
{
    private static string Format(string html) =>
        MarkupFormatter.Format(new HtmlParser().ParseDocument("<body>" + html + "</body>").Body!.ChildNodes);

    [Fact]
    public void Framework_attributes_go_and_significant_text_stays()
    {
        Assert.Equal(
            "<div>\n  <input aria-label=\"Name\">\n  <pre>  a\n    b</pre>\n  <p>1&nbsp;000</p>\n  <span data-btp-ignore>…</span>\n  <span data-btp-ignore>…</span>\n</div>\n",
            Format("<div _bl_2f3c9a1e-1111-2222-3333-444455556666 b-abcdef1234 blazor:onclick=\"1\"><input aria-label=\"Name\" _bl_9=\"\"><pre>  a\n    b</pre><p>1\u00A0000</p><span data-btp-ignore>x</span><span data-btp-ignore></span></div>"));
    }
}
