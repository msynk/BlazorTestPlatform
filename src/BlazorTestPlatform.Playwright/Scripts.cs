namespace BlazorTestPlatform.Playwright;

/// <summary>JavaScript snippets evaluated in the page.</summary>
internal static class Scripts
{
    /// <summary>
    /// Imports the workbench's locator engine (<c>btp-driver.js</c>) into the story frame. The in-app driver uses the same
    /// module, and it ports the in-memory engine's roles, accessible names, text and query rules, so all three drivers
    /// resolve locators alike.
    /// </summary>
    private const string Engine = "(await import(new URL('_content/BlazorTestPlatform.Workbench/btp-driver.js', document.baseURI).href))";

    /// <summary>Describes every element a locator chain (JSON) matches, as JSON.</summary>
    public const string Query = "async (steps) => " + Engine + ".queryJson(steps)";

    /// <summary>Returns the single element an action targets, or the problem to retry on (a string).</summary>
    public const string ActionTarget = "async ([steps, action]) => " + Engine + ".actionTargetJson(steps, action)";

    /// <summary>What Check/Uncheck must do on an element: <c>click</c>, nothing (null) or the problem.</summary>
    public const string CheckPlan = "async (element, want) => " + Engine + ".checkPlan(element, want)";

    /// <summary>Checked state, as JSON, of the last action's target (the locator chain re-resolves it after a re-render).</summary>
    public const string TargetChecked = "async (steps) => " + Engine + ".targetCheckedJson(steps)";

    /// <summary>
    /// Init script: Performance Timeline observers for layout shifts, the largest contentful paint and long tasks, buffered
    /// from navigation start. Entry types a browser does not support stay null.
    /// </summary>
    public const string VitalsObserver = """
        (() => {
            if (window.__btpVitals || typeof PerformanceObserver === 'undefined') return;
            const vitals = window.__btpVitals = { cls: null, lcp: null, longTasks: null, longTaskMs: null };
            const supported = type => (PerformanceObserver.supportedEntryTypes || []).includes(type);
            const observe = (type, onEntry) => new PerformanceObserver(list => list.getEntries().forEach(onEntry)).observe({ type, buffered: true });
            if (supported('layout-shift')) { vitals.cls = 0; observe('layout-shift', e => { if (!e.hadRecentInput) vitals.cls += e.value; }); }
            if (supported('largest-contentful-paint')) observe('largest-contentful-paint', e => { vitals.lcp = e.renderTime || e.loadTime || e.startTime; });
            if (supported('longtask')) { vitals.longTasks = 0; vitals.longTaskMs = 0; observe('longtask', e => { vitals.longTasks++; vitals.longTaskMs += e.duration; }); }
        })();
        """;

    /// <summary>Reads the observed metrics, the story root's element count and the heap size, as JSON.</summary>
    public const string Vitals = """
        () => {
            const vitals = window.__btpVitals || {};
            const root = document.querySelector('[data-btp-root]');
            return JSON.stringify({
                cumulativeLayoutShift: vitals.cls ?? null,
                largestContentfulPaintMilliseconds: vitals.lcp ?? null,
                longTasks: vitals.longTasks ?? null,
                longTaskMilliseconds: vitals.longTaskMs ?? null,
                domNodes: root ? root.getElementsByTagName('*').length : 0,
                jsHeapBytes: (performance.memory && performance.memory.usedJSHeapSize) || null,
            });
        }
        """;

    /// <summary>The story's accessibility tree as text (the port of <c>AriaSnapshot</c>).</summary>
    public const string AriaSnapshot = "async () => " + Engine + ".ariaSnapshot()";

    /// <summary>Describes the focused story element as JSON (null for the body or elements outside the story).</summary>
    public const string Focused = "async () => " + Engine + ".focusedJson()";

    /// <summary>CSS injected before snapshots: no animations, transitions or carets.</summary>
    public const string StabilizeCss = "*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;transition-duration:0s!important;transition-delay:0s!important;caret-color:transparent!important;scroll-behavior:auto!important}";

    /// <summary>
    /// Waits for fonts and images. Lazy images outside the viewport never load until scrolled to, so they are not
    /// awaited, and no single image can hold the run for more than 10 s (a stalled request).
    /// </summary>
    public const string WaitForAssets = """
        async () => {
          if (document.fonts && document.fonts.ready) { await document.fonts.ready; }
          const inViewport = i => {
            const r = i.getBoundingClientRect();
            return r.bottom >= 0 && r.right >= 0 && r.top <= innerHeight && r.left <= innerWidth;
          };
          const images = Array.from(document.images).filter(i => !i.complete && (i.loading !== 'lazy' || inViewport(i)));
          const settle = i => new Promise(r => {
            i.addEventListener('load', r, { once: true });
            i.addEventListener('error', r, { once: true });
            setTimeout(r, 10000);
          });
          await Promise.all(images.map(settle));
          await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          return true;
        }
        """;

    /// <summary>Returns the text of a story render error (StoryHost's error boundary or fixture failure), if any.</summary>
    public const string StoryError = """
        () => {
          const error = document.querySelector('[data-btp-root] [data-btp-error]');
          return error ? (error.textContent || 'The story failed to render').trim() : null;
        }
        """;

    /// <summary>Returns the visible Blazor error UI text, if any.</summary>
    public const string BlazorErrorUi = """
        () => {
          const ui = document.getElementById('blazor-error-ui');
          if (!ui) return null;
          const style = getComputedStyle(ui);
          return style.display !== 'none' && style.visibility !== 'hidden' ? (ui.textContent || 'Blazor error UI is visible').trim() : null;
        }
        """;
}
