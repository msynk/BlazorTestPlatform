// Browser APIs read by BrowserEmulationPanel; the stories emulate them with Geolocation(...), NetworkLatency(...) and
// ctx.SetOfflineAsync().

export function locate() {
    return new Promise((resolve, reject) => navigator.geolocation.getCurrentPosition(
        position => resolve([position.coords.latitude, position.coords.longitude]),
        error => reject(new Error(error.message || 'Position unavailable')),
        { timeout: 5000 }));
}

export function watchConnection(element) {
    const show = () => { element.textContent = navigator.onLine ? 'Online' : 'Offline'; };
    element.__btpConnection = show;
    window.addEventListener('online', show);
    window.addEventListener('offline', show);
    show();
}

export function unwatchConnection(element) {
    const show = element && element.__btpConnection;
    if (show) {
        window.removeEventListener('online', show);
        window.removeEventListener('offline', show);
    }
}

// Times a request for this module itself, bypassing the HTTP cache.
export async function measure() {
    const url = new URL(import.meta.url);
    url.searchParams.set('t', String(Date.now()));
    const started = performance.now();
    await fetch(url, { cache: 'no-store' });
    return performance.now() - started;
}
