// BlazorTestPlatform story-frame bridge.
// Exposes window.__btp so browser test runners (Playwright) can read the action log, advance fake time and wait for
// idleness through the same story instance that renders in the frame, under every interactive render mode.

export function register(dotNetRef, storyId, renderer) {
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
    };
    document.documentElement.setAttribute('data-btp-bridge', 'ready');
}
