// BlazorTestPlatform workbench shell.
// 1. Renders the result of an in-frame play run (▶ Run interactions).
// 2. Live arg controls: edits re-render interactive frames in place through window.__btp.setArgs, reload static
//    frames, and keep the shell URL (arg.Name=value) shareable. Without JavaScript the form still works (Apply).
// 3. The inspector: the frame's actions, mocked requests, JS calls, console and accessibility tree, and a recorder that
//    turns interactions in the frame into play-function code.
const panel = document.getElementById('btp-play-panel');

function render(steps, list) {
    for (const step of steps) {
        const item = document.createElement('li');
        item.dataset.failed = step.error ? 'true' : 'false';
        const kind = document.createElement('span');
        kind.className = 'btp-play__kind';
        kind.textContent = step.kind;
        const title = document.createElement('span');
        title.textContent = ' ' + step.title + ' ';
        const time = document.createElement('small');
        time.className = 'btp-muted';
        time.textContent = Math.round(step.durationMilliseconds || 0) + ' ms';
        item.append(kind, title, time);
        if (step.error) {
            const error = document.createElement('div');
            error.className = 'btp-play__error';
            error.textContent = step.error;
            item.append(error);
        }
        if (step.children && step.children.length) {
            const nested = document.createElement('ol');
            render(step.children, nested);
            item.append(nested);
        }
        list.append(item);
    }
}

if (panel) {
    window.addEventListener('message', event => {
        // Results come only from this workbench's own story frame: other windows and origins cannot fake a pass.
        const frame = document.querySelector('.btp-stage iframe');
        if (event.origin !== window.location.origin || !frame || event.source !== frame.contentWindow) return;
        const data = event.data;
        if (!data || data.source !== 'btp' || data.type !== 'play-result' || data.storyId !== panel.dataset.story) return;
        const status = panel.querySelector('.btp-play__status');
        status.dataset.status = data.status;
        status.textContent = data.status + (data.renderer ? ' · ' + data.renderer : '');
        const list = panel.querySelector('.btp-play__steps');
        list.replaceChildren();
        panel.querySelectorAll('.btp-play__error').forEach(previous => previous.remove());
        if (data.error) {
            const error = document.createElement('p');
            error.className = 'btp-play__error';
            error.textContent = data.error;
            panel.insertBefore(error, list);
        }
        render(data.steps || [], list);
    });
}

const PREFIX = 'arg.';
const controls = document.querySelector('[data-btp-controls]');

// Only controls that differ from what the story renders by default become overrides, so links stay minimal.
function changedArgs(form) {
    const args = new Map();
    for (const input of form.querySelectorAll('[name^="arg."]:not([type="hidden"])')) {
        const value = input.type === 'checkbox' ? (input.checked ? 'true' : 'false') : input.value;
        if (value !== (input.dataset.default ?? '')) {
            args.set(input.name.slice(PREFIX.length), value);
        }
        input.closest('tr')?.toggleAttribute('data-overridden', value !== (input.dataset.default ?? ''));
    }
    return args;
}

function withArgs(href, args) {
    const url = new URL(href, location.href);
    for (const key of [...url.searchParams.keys()]) {
        if (key.startsWith(PREFIX)) url.searchParams.delete(key);
    }
    for (const [name, value] of args) url.searchParams.append(PREFIX + name, value);
    return url;
}

function showErrors(form, message) {
    const list = form.querySelector('[data-btp-control-errors]');
    const errors = message ? message.split('\n').filter(Boolean) : [];
    list.replaceChildren(...errors.map(text => Object.assign(document.createElement('li'), { textContent: text })));
    list.hidden = errors.length === 0;
    for (const input of form.querySelectorAll('[name^="arg."]:not([type="hidden"])')) {
        const name = input.name.slice(PREFIX.length);
        const invalid = errors.some(text => text.startsWith("'" + name + "'"));
        if (invalid) input.setAttribute('aria-invalid', 'true'); else input.removeAttribute('aria-invalid');
    }
}

async function apply(form) {
    const args = changedArgs(form);
    const status = form.querySelector('[data-btp-control-status]');
    history.replaceState(null, '', withArgs(location.href, args));
    for (const link of document.querySelectorAll('a[data-btp-keep-args]')) {
        link.href = withArgs(link.getAttribute('href'), args).toString();
    }

    const frame = document.querySelector('.btp-stage iframe');
    if (!frame) return;
    const frameUrl = withArgs(frame.getAttribute('src'), args);
    let bridge = null;
    try {
        bridge = frame.contentWindow && frame.contentWindow.__btp;
    } catch {
        bridge = null;
    }

    if (bridge && bridge.setArgs && bridge.storyId === form.dataset.story) {
        const token = new URLSearchParams([...args]).toString();
        const errors = await bridge.setArgs(token);
        showErrors(form, errors);
        // A reload of the frame keeps the edits; the src attribute does not change, so nothing reloads now.
        frame.contentWindow.history.replaceState(null, '', frameUrl);
        status.textContent = errors ? 'Some values were rejected.' : 'Updated.';
    } else {
        // Static SSR (or not interactive yet): render the frame again with the new values.
        showErrors(form, '');
        frame.setAttribute('src', frameUrl.pathname + frameUrl.search);
        status.textContent = 'Reloaded.';
    }
}

if (controls) {
    let timer = 0;
    const schedule = delay => {
        clearTimeout(timer);
        timer = setTimeout(() => apply(controls), delay);
    };
    controls.addEventListener('input', event => schedule(event.target.matches('input[type="text"], input[type="number"]') ? 250 : 0));
    controls.addEventListener('change', () => schedule(0));
    controls.addEventListener('submit', event => {
        event.preventDefault();
        schedule(0);
    });
}

const inspector = document.getElementById('btp-inspector');

function frameBridge() {
    const frame = document.querySelector('.btp-stage iframe');
    try {
        const bridge = frame && frame.contentWindow && frame.contentWindow.__btp;
        return bridge && bridge.storyId === inspector.dataset.story ? bridge : null;
    } catch {
        return null;
    }
}

function fillList(name, items, describe) {
    inspector.querySelector(`[data-btp-count="${name}"]`).textContent = String(items.length);
    inspector.querySelector(`[data-btp-list="${name}"]`)
        .replaceChildren(...items.map(item => Object.assign(document.createElement('li'), { textContent: describe(item) })));
}

const playCode = lines => lines.length === 0
    ? ''
    : 'stories.Add("Recorded", s => s.Play(async ctx =>\n{\n' + lines.map(line => '    ' + line).join('\n') + '\n}));';

async function readJson(read, fallback) {
    try {
        return JSON.parse(await read()) ?? fallback;
    } catch {
        return fallback;
    }
}

async function refreshInspector() {
    const status = inspector.querySelector('[data-btp-inspector-status]');
    const bridge = frameBridge();
    if (!bridge) {
        status.textContent = 'Waiting for the story to become interactive…';
        return;
    }

    status.textContent = '';
    fillList('actions', await readJson(bridge.getActions, []), a => `${a.name}(${a.argumentJson ?? ''})`);
    fillList('requests', await readJson(bridge.getRequests, []), r => `${r.method} ${r.relativeUrl} → ${r.statusCode || '(no response)'}`);
    fillList('js', await readJson(bridge.getJSCalls, []), c => `${c.module ? c.module + ': ' : ''}${c.identifier}(${(c.arguments || []).join(', ')})${c.mocked ? '' : ' (not mocked)'}`);
    fillList('console', await readJson(bridge.getConsole, []), m => `[${m.level}] ${m.text}`);
    if (inspector.querySelector('[data-btp-aria-section]').open) {
        inspector.querySelector('[data-btp-aria]').textContent = (await bridge.getAriaSnapshot()) || '(no elements with a role)';
    }

    const recorded = await readJson(bridge.getRecording, { recording: false, lines: [] });
    const record = inspector.querySelector('[data-btp-record]');
    record.setAttribute('aria-pressed', recorded.recording ? 'true' : 'false');
    record.textContent = recorded.recording ? 'Stop recording' : 'Record';
    const code = playCode(recorded.lines);
    inspector.querySelector('[data-btp-recording]').textContent = code || (recorded.recording ? 'Recording… use the story in the frame.' : 'Press Record, then use the story in the frame.');
    inspector.querySelector('[data-btp-copy]').disabled = code.length === 0;
}

if (inspector) {
    inspector.querySelector('[data-btp-record]').addEventListener('click', async event => {
        const bridge = frameBridge();
        if (!bridge) return;
        await (event.currentTarget.getAttribute('aria-pressed') === 'true' ? bridge.stopRecording() : bridge.startRecording());
        await refreshInspector();
    });
    inspector.querySelector('[data-btp-copy]').addEventListener('click', async () => {
        const status = inspector.querySelector('[data-btp-inspector-status]');
        try {
            await navigator.clipboard.writeText(inspector.querySelector('[data-btp-recording]').textContent);
            status.textContent = 'Copied.';
        } catch {
            status.textContent = 'Copying is blocked here: select the code and copy it.';
        }
    });
    inspector.querySelector('[data-btp-aria-section]').addEventListener('toggle', () => refreshInspector());
    setInterval(() => { if (!document.hidden) refreshInspector(); }, 1000);
    refreshInspector();
}
