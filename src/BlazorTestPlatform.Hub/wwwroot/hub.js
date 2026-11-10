// BlazorTestPlatform hub: diff viewer modes, keyboard review, copy buttons and live run polling.
// Pages are server-rendered; this script only enhances them.

function setMode(viewer, mode) {
    viewer.dataset.mode = mode;
    viewer.querySelectorAll('[data-mode-button]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.modeButton === mode)));
    try { localStorage.setItem('btp-hub-viewer-mode', mode); } catch { /* private mode */ }
}

function initViewer(viewer) {
    let mode = 'split';
    try { mode = localStorage.getItem('btp-hub-viewer-mode') || mode; } catch { /* private mode */ }
    setMode(viewer, mode);
    viewer.querySelectorAll('[data-mode-button]').forEach(b => b.addEventListener('click', () => setMode(viewer, b.dataset.modeButton)));
    const split = viewer.querySelector('input[data-split]');
    split?.addEventListener('input', () => viewer.style.setProperty('--split', split.value + '%'));
    const opacity = viewer.querySelector('input[data-opacity]');
    opacity?.addEventListener('input', () => viewer.style.setProperty('--opacity', String(opacity.value / 100)));
    const zoom = viewer.querySelector('[data-zoom]');
    zoom?.addEventListener('click', () => {
        const box = viewer.querySelector('.focus-box');
        box?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
    });
    // Position the change box over each image once it has loaded (images scale down to fit).
    viewer.querySelectorAll('figure[data-bounds]').forEach(figure => {
        const img = figure.querySelector('img');
        const place = () => {
            const [x, y, w, h] = figure.dataset.bounds.split(',').map(Number);
            const scale = img.clientWidth / (img.naturalWidth || img.clientWidth || 1);
            let box = figure.querySelector('.focus-box');
            if (!box) { box = document.createElement('div'); box.className = 'focus-box'; figure.appendChild(box); }
            const top = img.offsetTop;
            Object.assign(box.style, { left: (img.offsetLeft + x * scale - 3) + 'px', top: (top + y * scale - 3) + 'px', width: (w * scale + 6) + 'px', height: (h * scale + 6) + 'px' });
        };
        if (img.complete) { place(); } else { img.addEventListener('load', place); }
        window.addEventListener('resize', place);
    });
}

function initReviewKeys() {
    const viewer = document.querySelector('[data-viewer]');
    if (!viewer) { return; }
    const modes = ['split', 'slider', 'overlay', 'strobe', 'diff'];
    document.addEventListener('keydown', e => {
        if (e.target.closest('input, textarea, select') || e.ctrlKey || e.metaKey || e.altKey) { return; }
        const link = selector => document.querySelector(selector);
        switch (e.key) {
            case 'j': link('[data-review-next]')?.click(); break;
            case 'k': link('[data-review-prev]')?.click(); break;
            case 'a': link('[data-review-accept]')?.click(); break;
            case 'r': link('[data-review-reject]')?.click(); break;
            case 'g': link('[data-review-accept-group]')?.click(); break;
            case '1': case '2': case '3': case '4': case '5': setMode(viewer, modes[Number(e.key) - 1]); break;
            default: return;
        }
        e.preventDefault();
    });
}

function initCopy() {
    document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
        const source = document.getElementById(button.dataset.copy);
        try {
            await navigator.clipboard.writeText(source.textContent);
            const text = button.textContent;
            button.textContent = 'Copied';
            setTimeout(() => { button.textContent = text; }, 1500);
        } catch {
            const range = document.createRange();
            range.selectNodeContents(source);
            getSelection().removeAllRanges();
            getSelection().addRange(range);
        }
    }));
}

function escapeHtml(text) {
    return String(text ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function initLive() {
    const panel = document.querySelector('[data-live]');
    if (!panel) { return; }
    const api = panel.dataset.live;
    const render = state => {
        if (!state) { return; }
        panel.querySelector('[data-live-label]').textContent = state.label;
        panel.querySelector('[data-live-status]').textContent = state.status;
        panel.querySelector('[data-live-status]').className = 'badge ' + (state.status === 'running' ? 'info' : state.status);
        panel.querySelector('[data-live-count]').textContent = `${state.completed} of ${state.total}`;
        panel.querySelector('[data-live-bar]').style.width = (state.total === 0 ? 100 : Math.round(100 * state.completed / state.total)) + '%';
        for (const name of ['passed', 'failed', 'flaky', 'skipped', 'quarantined']) {
            const el = panel.querySelector(`[data-live-${name}]`);
            if (el) { el.textContent = state[name]; }
        }
        panel.querySelector('[data-live-running]').textContent = state.running.length ? 'Running: ' + state.running.join(', ') : '';
        const rows = state.results.slice().reverse().slice(0, 200).map(r =>
            `<tr><td><span class="badge ${r.quarantined ? 'quarantined' : r.outcome.toLowerCase()}">${r.quarantined ? 'Quarantined' : escapeHtml(r.outcome)}</span></td>` +
            `<td class="title-cell"><a href="${escapeHtml(r.url)}"><strong>${escapeHtml(r.name)}</strong></a><span class="muted small">${escapeHtml(r.title)}</span>` +
            (r.error ? `<div class="small" style="color: var(--h-fail)">${escapeHtml(r.error)}</div>` : '') + `</td>` +
            `<td class="num">${r.duration} ms</td></tr>`).join('');
        panel.querySelector('[data-live-results]').innerHTML = rows || '<tr><td colspan="3" class="muted">Waiting for the first result…</td></tr>';
        const done = state.status !== 'running';
        panel.querySelector('[data-live-cancel]')?.toggleAttribute('hidden', done);
        panel.querySelector('[data-live-done]')?.toggleAttribute('hidden', !done);
        return done;
    };
    const tick = async () => {
        try {
            const response = await fetch(api, { headers: { accept: 'application/json' }, cache: 'no-store' });
            const done = render(await response.json());
            if (!done) { setTimeout(tick, 700); }
        } catch {
            setTimeout(tick, 2000);
        }
    };
    tick();
}

initCopy();
initLive();
document.querySelectorAll('[data-viewer]').forEach(initViewer);
initReviewKeys();
