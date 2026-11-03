// BlazorTestPlatform story-frame bridge.
// Exposes window.__btp so browser test runners (Playwright) can read the action log, advance fake time and wait for
// idleness through the same story instance that renders in the frame, under every interactive render mode; the
// workbench shell uses it for the inspector (actions, requests, JS calls, console, accessibility tree) and the recorder.

const driver = () => import('./btp-driver.js');

// Console messages and uncaught errors of the frame, for the workbench inspector (the last 200).
function captureConsole() {
    if (window.__btpConsole) return;
    const entries = window.__btpConsole = [];
    const add = (level, text) => {
        entries.push({ level, text, at: Date.now() });
        if (entries.length > 200) entries.shift();
    };
    const format = value => {
        if (typeof value === 'string') return value;
        if (value instanceof Error) return value.stack || value.message;
        try { return JSON.stringify(value); } catch { return String(value); }
    };
    for (const level of ['error', 'warn', 'info', 'log', 'debug']) {
        const original = console[level].bind(console);
        console[level] = (...args) => {
            add(level, args.map(format).join(' '));
            original(...args);
        };
    }
    window.addEventListener('error', event => add('error', event.message));
    window.addEventListener('unhandledrejection', event => add('error', 'Unhandled rejection: ' + format(event.reason)));
}

export function register(dotNetRef, storyId, renderer) {
    captureConsole();
    window.__btp = {
        storyId,
        renderer,
        getActions: () => dotNetRef.invokeMethodAsync('GetActions'),
        getRequests: () => dotNetRef.invokeMethodAsync('GetHttpRequests'),
        getJSCalls: () => dotNetRef.invokeMethodAsync('GetJSCalls'),
        setArgs: (token) => dotNetRef.invokeMethodAsync('SetArgs', token),
        hasFakeTime: () => dotNetRef.invokeMethodAsync('HasFakeTime'),
        advanceTime: (ms) => dotNetRef.invokeMethodAsync('AdvanceTime', ms),
        waitForIdle: () => dotNetRef.invokeMethodAsync('WaitForIdle'),
        // Workbench inspector and recorder (the same engine the drivers use).
        getConsole: () => JSON.stringify(window.__btpConsole || []),
        getAriaSnapshot: async () => (await driver()).ariaSnapshot(),
        startRecording: async () => (await driver()).startRecording(),
        stopRecording: async () => (await driver()).stopRecording(),
        getRecording: async () => (await driver()).recording(),
    };
    document.documentElement.setAttribute('data-btp-bridge', 'ready');
}
