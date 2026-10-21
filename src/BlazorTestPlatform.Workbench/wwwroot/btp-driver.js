// BlazorTestPlatform in-app driver.
// Runs play scripts inside the workbench frame: the C# play function executes in the Blazor runtime (Server circuit or
// WebAssembly) and drives the real DOM through these functions. Locator semantics mirror the bUnit and Playwright
// drivers: Testing-Library-style queries, strictness, actionability, browser-like default actions.

const normalize = s => (s || '').replace(/[\s​]+/g, ' ').trim();
const matches = (actual, expected, exact) => {
    const a = normalize(actual), e = normalize(expected);
    return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
};

const FOCUSABLE = 'a[href],area[href],button,input:not([type=hidden]),select,textarea,iframe,summary,[tabindex],[contenteditable]:not([contenteditable=false])';

function implicitRole(e) {
    const t = e.localName, type = (e.getAttribute('type') || 'text').toLowerCase();
    switch (t) {
        case 'a': case 'area': return e.hasAttribute('href') ? 'link' : 'generic';
        case 'button': return 'button';
        case 'input':
            if (['button', 'image', 'reset', 'submit'].includes(type)) return 'button';
            if (type === 'checkbox') return 'checkbox';
            if (type === 'radio') return 'radio';
            if (type === 'range') return 'slider';
            if (type === 'number') return 'spinbutton';
            if (type === 'search') return e.hasAttribute('list') ? 'combobox' : 'searchbox';
            if (type === 'hidden') return 'none';
            return e.hasAttribute('list') ? 'combobox' : 'textbox';
        case 'textarea': return 'textbox';
        case 'select': return (e.multiple || e.size > 1) ? 'listbox' : 'combobox';
        case 'h1': case 'h2': case 'h3': case 'h4': case 'h5': case 'h6': return 'heading';
        case 'ul': case 'ol': case 'menu': return 'list';
        case 'li': return 'listitem';
        case 'img': return e.getAttribute('alt') === '' ? 'presentation' : 'img';
        case 'nav': return 'navigation';
        case 'main': return 'main';
        case 'dialog': return 'dialog';
        case 'table': return 'table';
        case 'tr': return 'row';
        case 'td': return 'cell';
        case 'th': return 'columnheader';
        case 'form': return 'form';
        case 'p': return 'paragraph';
        case 'hr': return 'separator';
        case 'progress': return 'progressbar';
        case 'output': return 'status';
        case 'option': return 'option';
        case 'fieldset': case 'details': case 'optgroup': return 'group';
        case 'article': return 'article';
        case 'aside': return 'complementary';
        case 'section': return (e.hasAttribute('aria-label') || e.hasAttribute('aria-labelledby')) ? 'region' : 'generic';
        case 'div': case 'span': return 'generic';
        default: return null;
    }
}

function roleOf(e) {
    const explicit = (e.getAttribute('role') || '').trim().split(/\s+/)[0];
    return explicit || implicitRole(e);
}

function isVisible(e) {
    if (!e.isConnected) return false;
    if (e.checkVisibility && !e.checkVisibility({ checkVisibilityCSS: true })) return false;
    const r = e.getBoundingClientRect();
    return r.width > 0 || r.height > 0 || e.getClientRects().length > 0;
}

function hiddenFromAccessibility(e) {
    if (!isVisible(e) && !(e.localName === 'option')) return true;
    for (let c = e; c; c = c.parentElement) {
        if (c.getAttribute && (c.getAttribute('aria-hidden') === 'true' || c.hasAttribute('inert'))) return true;
    }
    return false;
}

const NAME_FROM_CONTENT = ['button', 'cell', 'checkbox', 'columnheader', 'gridcell', 'heading', 'link', 'menuitem', 'menuitemcheckbox',
    'menuitemradio', 'option', 'radio', 'row', 'rowheader', 'switch', 'tab', 'tooltip', 'treeitem', 'caption'];

function contentText(e) {
    let text = '';
    for (const child of e.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) text += child.textContent;
        else if (child.nodeType === Node.ELEMENT_NODE && !hiddenFromAccessibility(child)) {
            const label = child.getAttribute('aria-label');
            text += ' ' + (label && label.trim() ? label : (child.localName === 'img' ? (child.getAttribute('alt') || '') : contentText(child))) + ' ';
        }
    }
    return text;
}

function accessibleName(e) {
    const by = e.getAttribute('aria-labelledby');
    if (by) {
        const parts = by.split(/\s+/).map(id => document.getElementById(id)).filter(Boolean).map(n => normalize(n.textContent));
        if (parts.length) return normalize(parts.join(' '));
    }
    const label = e.getAttribute('aria-label');
    if (label && label.trim()) return normalize(label);
    if (e.labels && e.labels.length) return normalize(Array.from(e.labels).map(l => l.textContent).join(' '));
    if (e.localName === 'img' || e.localName === 'area') return normalize(e.getAttribute('alt'));
    if (e.localName === 'input' && ['button', 'submit', 'reset'].includes((e.type || '').toLowerCase()))
        return normalize(e.value || (e.type === 'submit' ? 'Submit' : e.type === 'reset' ? 'Reset' : ''));
    if (e.localName === 'fieldset') { const l = e.querySelector(':scope > legend'); if (l) return normalize(l.textContent); }
    const role = roleOf(e);
    if (role && NAME_FROM_CONTENT.includes(role)) { const c = normalize(contentText(e)); if (c) return c; }
    return normalize(e.getAttribute('title') || e.getAttribute('placeholder') || '');
}

function labelMatches(e, text, exact) {
    const aria = e.getAttribute('aria-label');
    if (aria && matches(aria, text, exact)) return true;
    const by = e.getAttribute('aria-labelledby');
    if (by) {
        const t = by.split(/\s+/).map(id => document.getElementById(id)).filter(Boolean).map(n => n.textContent).join(' ');
        if (t && matches(t, text, exact)) return true;
    }
    return !!(e.labels && Array.from(e.labels).some(l => matches(l.textContent, text, exact)));
}

function textFor(e) {
    if (e.localName === 'input' && ['button', 'submit', 'reset'].includes((e.type || '').toLowerCase())) return e.value || '';
    return e.textContent;
}

function innermost(list) {
    return list.filter(c => !list.some(o => o !== c && c.contains(o)));
}

function query(scopeElements, q) {
    const candidates = scopeElements === null
        ? Array.from(document.body.querySelectorAll('*'))
        : scopeElements.flatMap(p => Array.from(p.querySelectorAll('*')));
    switch (q.type) {
        case 'css':
            return scopeElements === null
                ? Array.from(document.querySelectorAll(q.selector))
                : [...new Set(scopeElements.flatMap(p => Array.from(p.querySelectorAll(q.selector))))];
        case 'role':
            return candidates.filter(e => roleOf(e) === q.role && !hiddenFromAccessibility(e) && (q.name == null || matches(accessibleName(e), q.name, q.exact)));
        case 'text':
            return innermost(candidates.filter(e => !['script', 'style', 'template', 'head'].includes(e.localName) && matches(textFor(e), q.value, q.exact)));
        case 'label':
            return candidates.filter(e => labelMatches(e, q.value, q.exact));
        case 'placeholder':
            return candidates.filter(e => e.hasAttribute('placeholder') && matches(e.getAttribute('placeholder'), q.value, q.exact));
        case 'testid':
            return candidates.filter(e => e.getAttribute(q.attribute) === q.value);
        case 'alt':
            return candidates.filter(e => ['img', 'area', 'input'].includes(e.localName) && e.hasAttribute('alt') && matches(e.getAttribute('alt'), q.value, q.exact));
        case 'title':
            return candidates.filter(e => e.hasAttribute('title') && matches(e.getAttribute('title'), q.value, q.exact));
        default:
            throw new Error('Unsupported query ' + q.type);
    }
}

function resolve(steps) {
    let current = null;
    for (const step of steps) {
        if (step.kind === 'find') current = query(current, step.query);
        else if (step.kind === 'nth') {
            current = current || Array.from(document.body.querySelectorAll('*'));
            const i = step.index < 0 ? current.length + step.index : step.index;
            current = i >= 0 && i < current.length ? [current[i]] : [];
        } else if (step.kind === 'hasText') current = (current || []).filter(e => matches(e.textContent, step.text, false));
        else if (step.kind === 'hasNotText') current = (current || []).filter(e => !matches(e.textContent, step.text, false));
    }
    return current || [];
}

function isDisabled(e) {
    return (e.matches && e.matches(':disabled')) || e.getAttribute('aria-disabled') === 'true';
}

function isEditable(e) {
    if (isDisabled(e) || e.readOnly) return false;
    if (e.isContentEditable || e.localName === 'textarea') return true;
    return e.localName === 'input' && ['text', 'search', 'email', 'url', 'tel', 'password', 'number', 'date', 'datetime-local', 'month', 'time', 'week']
        .includes((e.getAttribute('type') || 'text').toLowerCase());
}

function checkedState(e, role) {
    if (e.localName === 'input' && (e.type === 'checkbox' || e.type === 'radio')) return !!e.checked;
    if (['checkbox', 'radio', 'switch', 'menuitemcheckbox', 'menuitemradio'].includes(role)) return e.getAttribute('aria-checked') === 'true';
    if (role === 'option' || role === 'treeitem') { const s = e.getAttribute('aria-selected'); return s === null ? null : s === 'true'; }
    if (role === 'button' && e.hasAttribute('aria-pressed')) return e.getAttribute('aria-pressed') === 'true';
    return null;
}

function describe(e) {
    const role = roleOf(e);
    const attrs = {};
    for (const a of e.attributes) if (!a.name.startsWith('_bl_')) attrs[a.name] = a.value;
    let value = null;
    if ('value' in e && ['input', 'textarea', 'select'].includes(e.localName)) value = e.value;
    else if (e.isContentEditable) value = e.textContent;
    const html = e.outerHTML.replace(/\s_bl_[\w-]+(="[^"]*")?/g, '');
    return {
        tagName: e.localName, role, accessibleName: accessibleName(e), text: normalize(e.textContent), value, attributes: attrs,
        isVisible: isVisible(e), isEnabled: !isDisabled(e), isChecked: checkedState(e, role), isFocused: document.activeElement === e,
        isEditable: isEditable(e), html: html.length > 300 ? html.slice(0, 300) + '…' : html
    };
}

// ---- events -------------------------------------------------------------------------------------------------------

const MODS = m => ({ altKey: !!(m & 1), ctrlKey: !!(m & 2), metaKey: !!(m & 4), shiftKey: !!(m & 8) });

function mouse(e, type, init) {
    const ctor = type.startsWith('pointer') ? PointerEvent : MouseEvent;
    return e.dispatchEvent(new ctor(type, { bubbles: true, cancelable: true, composed: true, view: window, pointerId: 1, pointerType: 'mouse', isPrimary: true, ...init }));
}

function key(e, type, k, mods) {
    const domKey = k === 'Space' ? ' ' : k;
    const code = k === 'Space' || k === ' ' ? 'Space' : (/^[a-z]$/i.test(k) ? 'Key' + k.toUpperCase() : (/^[0-9]$/.test(k) ? 'Digit' + k : k));
    return e.dispatchEvent(new KeyboardEvent(type, { key: domKey, code, bubbles: true, cancelable: true, composed: true, ...MODS(mods) }));
}

function setValue(e, value) {
    const proto = e.localName === 'textarea' ? HTMLTextAreaElement.prototype : e.localName === 'select' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
    setter.call(e, value);
}

function edited(e) {
    if (e.__btpValueAtFocus === undefined) e.__btpValueAtFocus = e.__btpLastCommitted ?? '';
    e.__btpDirty = true;
}

// Browsers fire `change` when a user-edited control loses focus; scripted edits don't, so emulate it.
document.addEventListener('focusout', ev => {
    const e = ev.target;
    if (e && e.__btpDirty) {
        e.__btpDirty = false;
        e.__btpLastCommitted = e.value;
        e.dispatchEvent(new Event('change', { bubbles: true }));
    }
}, true);

function commit(e) {
    if (e && e.__btpDirty) {
        e.__btpDirty = false;
        e.__btpLastCommitted = e.value;
        e.dispatchEvent(new Event('change', { bubbles: true }));
    }
}

function focusable(e) {
    return e.matches(FOCUSABLE) && !isDisabled(e) && isVisible(e);
}

function tabOrder() {
    const all = Array.from(document.querySelectorAll(FOCUSABLE)).filter(e => focusable(e) && e.tabIndex >= 0 && !e.closest('[inert]'));
    const radiosSeen = new Set();
    const filtered = all.filter(e => {
        if (e.localName === 'input' && e.type === 'radio' && e.name) {
            const group = all.filter(r => r.localName === 'input' && r.type === 'radio' && r.name === e.name);
            const rep = group.find(r => r.checked) || group[0];
            if (rep !== e || radiosSeen.has(e.name)) return false;
            radiosSeen.add(e.name);
        }
        return true;
    });
    return filtered.map((e, i) => ({ e, i })).sort((a, b) => {
        const pa = a.e.tabIndex > 0 ? 0 : 1, pb = b.e.tabIndex > 0 ? 0 : 1;
        return pa - pb || (pa === 0 ? a.e.tabIndex - b.e.tabIndex : 0) || a.i - b.i;
    }).map(x => x.e);
}

function moveFocus(backwards) {
    const order = tabOrder();
    const active = document.activeElement && document.activeElement !== document.body ? document.activeElement : null;
    let index = active ? order.indexOf(active) : -1;
    let next;
    if (index < 0) {
        if (!active) next = backwards ? order.length - 1 : 0;
        else {
            const after = order.findIndex(e => active.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING);
            next = backwards ? (after < 0 ? order.length - 1 : after - 1) : after;
        }
    } else next = backwards ? index - 1 : index + 1;
    if (next < 0 || next >= order.length || next === undefined) { if (active) active.blur(); return; }
    order[next].focus();
}

function buttonLike(e) {
    return e.localName === 'button' || e.localName === 'summary' ||
        (e.localName === 'input' && ['button', 'submit', 'reset', 'checkbox', 'radio', 'image'].includes((e.type || '').toLowerCase()));
}

function insertText(e, ch) {
    edited(e);
    if (e.isContentEditable) e.textContent += ch; else setValue(e, (e.value || '') + ch);
    e.dispatchEvent(new InputEvent('input', { bubbles: true, data: ch, inputType: 'insertText' }));
}

export function pressKey(k, mods) {
    const target = document.activeElement || document.body;
    const printable = (k.length === 1 || k === 'Space') && !(mods & (1 | 2 | 4));
    const notPrevented = key(target, 'keydown', k, mods);
    if (printable) key(target, 'keypress', k, mods);
    if (notPrevented) {
        if (k === 'Tab') moveFocus(!!(mods & 8));
        else if (k === 'Enter') {
            if (buttonLike(target) || (target.localName === 'a' && target.hasAttribute('href'))) target.click();
            else if (target.localName === 'input' && isEditable(target) && target.form) { commit(target); target.form.requestSubmit(); }
        } else if (isEditable(target) && printable) insertText(target, k === 'Space' ? ' ' : k);
        else if (isEditable(target) && k === 'Backspace' && (target.value || '').length > 0) {
            edited(target);
            setValue(target, target.value.slice(0, -1));
            target.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward' }));
        }
    }
    key(document.activeElement || document.body, 'keyup', k, mods);
    if (notPrevented && (k === 'Space' || k === ' ') && buttonLike(target) && document.activeElement === target) target.click();
}

// ---- API used by the C# driver -------------------------------------------------------------------------------------

export function queryAll(steps) {
    return resolve(steps).map(describe);
}

export function focused() {
    const e = document.activeElement;
    return !e || e === document.body || e === document.documentElement ? null : describe(e);
}

const NEEDS_ENABLED = ['click', 'dblclick', 'fill', 'clear', 'type', 'check', 'uncheck', 'select'];

/** Performs an action; returns null on success or a problem description to retry on. */
export function perform(steps, action) {
    const found = resolve(steps);
    if (found.length !== 1) return found.length === 0 ? 'no element matches' : `strict mode violation: ${found.length} elements match`;
    const e = found[0];
    if (!action.force && !['focus', 'blur', 'scroll'].includes(action.kind)) {
        if (!isVisible(e)) return 'element is not visible';
        if (NEEDS_ENABLED.includes(action.kind) && isDisabled(e)) return 'element is disabled';
    }
    const mods = action.modifiers || 0, button = action.button || 0;
    switch (action.kind) {
        case 'click': case 'dblclick': {
            e.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            mouse(e, 'pointerover', {}); mouse(e, 'mouseover', {});
            const clicks = action.kind === 'dblclick' ? 2 : 1;
            for (let i = 1; i <= clicks; i++) {
                mouse(e, 'pointerdown', { button, buttons: 1, ...MODS(mods) });
                mouse(e, 'mousedown', { button, buttons: 1, detail: i, ...MODS(mods) });
                const target = e.closest(FOCUSABLE);
                if (target && focusable(target)) target.focus(); else if (document.activeElement) { commit(document.activeElement); document.activeElement.blur(); }
                mouse(e, 'pointerup', { button, ...MODS(mods) });
                mouse(e, 'mouseup', { button, detail: i, ...MODS(mods) });
                if (button === 2) mouse(e, 'contextmenu', { button, detail: i, ...MODS(mods) });
                else if (mods === 0 && button === 0) e.click();
                else mouse(e, 'click', { button, detail: i, ...MODS(mods) });
            }
            if (clicks === 2) mouse(e, 'dblclick', { button, detail: 2, ...MODS(mods) });
            return null;
        }
        case 'hover':
            for (const t of ['pointerover', 'pointerenter', 'mouseover', 'mouseenter', 'pointermove', 'mousemove']) mouse(e, t, {});
            return null;
        case 'focus': e.focus(); return null;
        case 'blur': commit(e); e.blur(); return null;
        case 'fill': case 'clear': {
            if (!isEditable(e) && !(e.localName === 'input' && ['color', 'range'].includes(e.type))) return 'element is not editable';
            e.focus();
            edited(e);
            if (e.isContentEditable) e.textContent = action.text || ''; else setValue(e, action.kind === 'clear' ? '' : (action.text || ''));
            e.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertReplacementText' }));
            return null;
        }
        case 'type':
            e.focus();
            for (const ch of (action.text || '')) pressKey(ch, 0);
            return null;
        case 'press': {
            e.focus();
            const parts = action.text.split('+');
            const k = parts.pop() || '+';
            let m = 0;
            for (const p of parts) m |= p === 'Shift' ? 8 : (p === 'Control' || p === 'Ctrl') ? 2 : p === 'Alt' ? 1 : 4;
            pressKey(k, m);
            return null;
        }
        case 'check': case 'uncheck': {
            const want = action.kind === 'check';
            const state = checkedState(e, roleOf(e));
            if (state === null) return 'element is not a checkbox, radio or switch';
            if (state !== want) e.click();
            return checkedState(e, roleOf(e)) === want ? null : 'clicking did not change the checked state';
        }
        case 'select': {
            if (e.localName !== 'select') return 'element is not a <select>';
            const options = Array.from(e.options);
            const wanted = action.values.map(v => options.find(o => o.value === v) || options.find(o => normalize(o.textContent) === v));
            if (wanted.some(o => !o)) return 'no option matches ' + action.values.join(', ');
            e.focus();
            options.forEach(o => { o.selected = wanted.includes(o); });
            e.dispatchEvent(new Event('input', { bubbles: true }));
            e.dispatchEvent(new Event('change', { bubbles: true }));
            return null;
        }
        case 'pointerdown': mouse(e, 'pointerdown', { button, buttons: 1 }); mouse(e, 'mousedown', { button, buttons: 1 }); return null;
        case 'pointerup': mouse(e, 'pointerup', { button }); mouse(e, 'mouseup', { button }); return null;
        case 'drag': {
            const targets = resolve(action.target);
            if (targets.length !== 1) return 'drop target must match exactly one element';
            const data = new DataTransfer();
            const drag = (el, type) => el.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: data }));
            drag(e, 'dragstart'); drag(targets[0], 'dragenter'); drag(targets[0], 'dragover'); drag(targets[0], 'drop'); drag(e, 'dragend');
            return null;
        }
        case 'scroll': e.scrollIntoView({ block: 'center' }); return null;
        default: return 'unsupported action ' + action.kind;
    }
}

export function keyboard(kind, value) {
    const parts = value.length > 1 ? value.split('+') : [value];
    const k = parts.pop() || '+';
    let m = 0;
    for (const p of parts) m |= p === 'Shift' ? 8 : (p === 'Control' || p === 'Ctrl') ? 2 : p === 'Alt' ? 1 : 4;
    const target = document.activeElement || document.body;
    if (kind === 'press') pressKey(k, m);
    else if (kind === 'type') for (const ch of value) pressKey(ch, 0);
    else if (kind === 'down') key(target, 'keydown', k, m);
    else if (kind === 'up') key(target, 'keyup', k, m);
}

export function settle() {
    return new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true))));
}

export function markup() {
    const root = document.querySelector('[data-btp-root]');
    return root ? root.innerHTML : '';
}

export function publishResult(storyId, result) {
    const root = document.querySelector('[data-btp-root]');
    if (root) root.setAttribute('data-btp-play-status', result.status);
    window.__btpPlayResult = result;
    if (window.parent && window.parent !== window) window.parent.postMessage({ source: 'btp', type: 'play-result', storyId, ...result }, '*');
}

// JSON entry points (the .NET side passes and receives strings, which keeps interop trimming-safe).
export function queryJson(stepsJson) { return JSON.stringify(queryAll(JSON.parse(stepsJson))); }
export function performJson(stepsJson, actionJson) {
    const action = JSON.parse(actionJson);
    if (action.target) action.target = action.target;
    return perform(JSON.parse(stepsJson), action);
}
export function focusedJson() { const f = focused(); return f ? JSON.stringify(f) : null; }
export function publishResultJson(storyId, resultJson) { publishResult(storyId, JSON.parse(resultJson)); }
