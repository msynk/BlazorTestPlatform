// BlazorTestPlatform workbench shell.
// 1. Renders the result of an in-frame play run (▶ Run interactions).
// 2. Live arg controls: edits re-render interactive frames in place through window.__btp.setArgs, reload static
//    frames, and keep the shell URL (arg.Name=value) shareable. Without JavaScript the form still works (Apply).
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
        const data = event.data;
        if (!data || data.source !== 'btp' || data.type !== 'play-result' || data.storyId !== panel.dataset.story) return;
        const status = panel.querySelector('.btp-play__status');
        status.dataset.status = data.status;
        status.textContent = data.status + (data.renderer ? ' · ' + data.renderer : '');
        const list = panel.querySelector('.btp-play__steps');
        list.replaceChildren();
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
