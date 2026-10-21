// BlazorTestPlatform locator engine and in-app driver.
// The locator engine resolves play-script locators against the real DOM with the same semantics as the in-memory
// engine (BlazorTestPlatform.Accessibility: AriaRoles, AccessibleName, DomFacts.TextOf, DomQueries), so roles,
// accessible names, text and query results agree across drivers. Both browser drivers use it: the in-app driver runs
// play scripts inside the workbench frame (the C# play function executes in the Server circuit or WebAssembly runtime
// and drives the DOM through the functions below), and the Playwright driver imports this module into the frame to
// resolve locators before acting with real input.
// Queries see only the story: descendants of [data-btp-root], without the workbench's [data-btp-arg-errors] list.

const ROOT = '[data-btp-root]';
const OUTSIDE_STORY = '[data-btp-arg-errors]';

// ---- text ----------------------------------------------------------------------------------------------------------

// TextMatch.Normalize: .NET whitespace plus zero-width spaces collapse to one space.
const WHITESPACE = /[\t\n\v\f\r \u0085\u00a0\u1680\u2000-\u200b\u2028\u2029\u202f\u205f\u3000]+/g;
const normalize = s => (s || '').replace(WHITESPACE, ' ').replace(/^ | $/g, '');
const matches = (actual, expected, exact) => {
    const a = normalize(actual), e = normalize(expected);
    return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
};
const blank = s => !s || !s.trim();

// DomFacts.TextOf: text content without non-rendered elements, with a space after line-breaking elements, so
// <div>A</div><div>B</div> reads "A B".
const NEVER_RENDERED = new Set(['script', 'style', 'template', 'head', 'noscript', 'meta', 'link', 'title']);
const TEXT_BREAKS = new Set(['br', 'p', 'div', 'li']);

function appendText(node, out) {
    if (node.nodeType === Node.ELEMENT_NODE && NEVER_RENDERED.has(node.localName)) return;
    if (node.nodeType === Node.TEXT_NODE) { out.push(node.textContent); return; }
    for (const child of node.childNodes) {
        appendText(child, out);
        if (child.nodeType === Node.ELEMENT_NODE && TEXT_BREAKS.has(child.localName)) out.push(' ');
    }
}

function textOf(node) {
    const out = [];
    appendText(node, out);
    return normalize(out.join(''));
}

// ---- scope ---------------------------------------------------------------------------------------------------------

function storyRoot() {
    return document.querySelector(ROOT) || document.body;
}

function inStory(e, root) {
    return e !== root && root.contains(e) && !e.closest(OUTSIDE_STORY);
}

function storyElements(root) {
    return Array.from(root.querySelectorAll('*')).filter(e => !e.closest(OUTSIDE_STORY));
}

function byId(root, id) {
    const e = root.querySelector('#' + CSS.escape(id));
    return e && inStory(e, root) ? e : null;
}

function idRefs(root, value) {
    return blank(value) ? [] : value.split(' ').filter(Boolean).map(id => byId(root, id)).filter(Boolean);
}

// ---- visibility, focus and state -----------------------------------------------------------------------------------

const VISIBILITY_OPTIONS = { visibilityProperty: true, checkVisibilityCSS: true };

/**
 * Not rendered (DomFacts.IsHidden, from computed styles): display:none or visibility:hidden here or above, closed
 * details and dialogs, hidden inputs. Size does not matter: empty and visually hidden elements are still rendered.
 */
function isHidden(e) {
    if (!e.isConnected) return true;
    // A collapsed <select> renders no boxes for its options, but they are still part of the control.
    if (e.localName === 'option' || e.localName === 'optgroup') { const select = e.closest('select'); if (select) return isHidden(select); }
    const style = getComputedStyle(e);
    if (style.display === 'contents') {
        // No box of its own (so checkVisibility() is false), but its content renders.
        return style.visibility === 'hidden' || style.visibility === 'collapse' || (!!e.parentElement && isHidden(e.parentElement));
    }
    if (e.checkVisibility) return !e.checkVisibility(VISIBILITY_OPTIONS);
    if (style.visibility === 'hidden' || style.visibility === 'collapse') return true;
    for (let c = e; c; c = c.parentElement) if (getComputedStyle(c).display === 'none') return true;
    return false;
}

/** DomFacts.IsExcludedFromAccessibilityTree. */
function isExcluded(e) {
    return isHidden(e) || !!e.closest('[aria-hidden="true" i], [inert]');
}

function visibleText(node) {
    const range = document.createRange();
    range.selectNode(node);
    const rect = range.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
}

/** Visible as a user sees it (Playwright's definition): rendered, visibility:visible and a non-empty box. */
function isVisible(e) {
    if (!e.isConnected) return false;
    const style = getComputedStyle(e);
    if (style.display === 'contents') {
        for (let c = e.firstChild; c; c = c.nextSibling) {
            if (c.nodeType === Node.ELEMENT_NODE && isVisible(c)) return true;
            if (c.nodeType === Node.TEXT_NODE && visibleText(c)) return true;
        }
        return false;
    }
    if (e.checkVisibility && !e.checkVisibility()) return false;
    if (style.visibility !== 'visible') return false;
    const rect = e.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
}

const isAriaTrue = (e, name) => (e.getAttribute(name) || '').toLowerCase() === 'true';

function isDisabled(e) {
    return (e.matches && e.matches(':disabled')) || isAriaTrue(e, 'aria-disabled');
}

const isContentEditable = e => e.hasAttribute('contenteditable') && e.getAttribute('contenteditable').toLowerCase() !== 'false';
const TEXT_INPUT_TYPES = ['text', 'search', 'email', 'url', 'tel', 'password', 'number', 'date', 'datetime-local', 'month', 'time', 'week'];
const isTextInput = e => e.localName === 'input' && TEXT_INPUT_TYPES.includes(e.type);

function isEditable(e) {
    if (isDisabled(e) || e.hasAttribute('readonly')) return false;
    return e.localName === 'textarea' || isContentEditable(e) || isTextInput(e);
}

function tabIndexAttribute(e) {
    const value = e.getAttribute('tabindex');
    if (value === null) return null;
    const parsed = parseInt(value, 10);
    return Number.isNaN(parsed) ? null : parsed;
}

/** Only the first <summary> child of a <details> is its summary (and focusable). */
const isDetailsSummary = e => e.localName === 'summary' && e.parentElement?.localName === 'details' && e.parentElement.querySelector(':scope > summary') === e;
const isLink = e => (e.localName === 'a' || e.localName === 'area') && e.hasAttribute('href');

function isNativelyFocusable(e) {
    switch (e.localName) {
        case 'a': case 'area': return e.hasAttribute('href');
        case 'button': case 'select': case 'textarea': case 'iframe': case 'embed': case 'object': return true;
        case 'input': return e.type !== 'hidden';
        case 'summary': return isDetailsSummary(e);
        case 'audio': case 'video': return e.hasAttribute('controls');
        default: return isContentEditable(e);
    }
}

/** As browsers decide it: rendered, not inert, not disabled (a disabled fieldset disables controls outside its first legend). */
function isFocusable(e) {
    if (isHidden(e) || e.closest('[inert]') || e.matches(':disabled')) return false;
    return tabIndexAttribute(e) !== null || isNativelyFocusable(e);
}

function isTabbable(e) {
    if (!isFocusable(e)) return false;
    const tabIndex = tabIndexAttribute(e);
    return tabIndex === null || tabIndex >= 0;
}

/** DomQueries.CheckedState: native checkboxes and radios, then aria-checked/aria-selected/aria-pressed. */
function checkedState(e, role) {
    if (e.localName === 'input' && (e.type === 'checkbox' || e.type === 'radio')) return !!e.checked;
    if (['checkbox', 'radio', 'switch', 'menuitemcheckbox', 'menuitemradio', 'option', 'treeitem'].includes(role)) {
        const state = e.getAttribute('aria-checked') ?? (role === 'option' || role === 'treeitem' ? e.getAttribute('aria-selected') : null);
        return state !== null ? state.toLowerCase() === 'true' : (['checkbox', 'radio', 'switch'].includes(role) ? false : null);
    }
    if (role === 'button' && e.hasAttribute('aria-pressed')) return isAriaTrue(e, 'aria-pressed');
    return null;
}

// ---- roles (AriaRoles) ---------------------------------------------------------------------------------------------

const VALID_ROLES = new Set([
    'alert', 'alertdialog', 'application', 'article', 'banner', 'blockquote', 'button', 'caption', 'cell', 'checkbox',
    'code', 'columnheader', 'combobox', 'complementary', 'contentinfo', 'definition', 'deletion', 'dialog', 'directory',
    'document', 'emphasis', 'feed', 'figure', 'form', 'generic', 'grid', 'gridcell', 'group', 'heading', 'img', 'image',
    'insertion', 'link', 'list', 'listbox', 'listitem', 'log', 'main', 'mark', 'marquee', 'math', 'menu', 'menubar',
    'menuitem', 'menuitemcheckbox', 'menuitemradio', 'meter', 'navigation', 'none', 'note', 'option', 'paragraph',
    'presentation', 'progressbar', 'radio', 'radiogroup', 'region', 'row', 'rowgroup', 'rowheader', 'scrollbar', 'search',
    'searchbox', 'separator', 'slider', 'spinbutton', 'status', 'strong', 'subscript', 'superscript', 'switch', 'tab',
    'table', 'tablist', 'tabpanel', 'term', 'textbox', 'time', 'timer', 'toolbar', 'tooltip', 'tree', 'treegrid', 'treeitem',
]);
const ABSTRACT_ROLES = new Set(['command', 'composite', 'input', 'landmark', 'range', 'roletype', 'section', 'sectionhead', 'select', 'structure', 'widget', 'window']);
const NAME_FROM_CONTENT = new Set(['button', 'cell', 'checkbox', 'columnheader', 'gridcell', 'heading', 'link', 'menuitem', 'menuitemcheckbox',
    'menuitemradio', 'option', 'radio', 'row', 'rowheader', 'switch', 'tab', 'tooltip', 'treeitem', 'caption']);
const GLOBAL_ARIA = ['aria-label', 'aria-labelledby', 'aria-describedby', 'aria-live', 'aria-owns', 'aria-controls', 'aria-details', 'aria-keyshortcuts', 'aria-roledescription'];
const SIMPLE_ROLES = {
    article: 'article', aside: 'complementary', blockquote: 'blockquote', button: 'button', caption: 'caption', code: 'code',
    datalist: 'listbox', dd: 'definition', del: 'deletion', details: 'group', dfn: 'term', dt: 'term', dialog: 'dialog',
    em: 'emphasis', fieldset: 'group', figure: 'figure', form: 'form', h1: 'heading', h2: 'heading', h3: 'heading',
    h4: 'heading', h5: 'heading', h6: 'heading', hr: 'separator', ins: 'insertion', li: 'listitem', main: 'main',
    mark: 'mark', math: 'math', menu: 'list', ol: 'list', ul: 'list', meter: 'meter', nav: 'navigation', optgroup: 'group',
    option: 'option', output: 'status', p: 'paragraph', progress: 'progressbar', search: 'search', strong: 'strong',
    sub: 'subscript', sup: 'superscript', svg: 'graphics-document', table: 'table', tbody: 'rowgroup', thead: 'rowgroup',
    tfoot: 'rowgroup', td: 'cell', textarea: 'textbox', time: 'time', tr: 'row', div: 'generic', span: 'generic',
    b: 'generic', i: 'generic', u: 'generic', small: 'generic', bdi: 'generic', bdo: 'generic', data: 'generic',
    samp: 'generic', q: 'generic', s: 'generic', pre: 'generic',
};

const isValidRole = r => VALID_ROLES.has(r) || r.startsWith('doc-') || r.startsWith('graphics-');

function explicitRole(e) {
    const value = e.getAttribute('role');
    if (blank(value)) return null;
    for (const token of value.split(' ')) {
        const role = token.toLowerCase();
        if (role && isValidRole(role) && !ABSTRACT_ROLES.has(role)) return role;
    }
    return null;
}

const inSectioningContent = e => !!e.parentElement?.closest('article, aside, main, nav, section');

function inputRole(e) {
    const type = (e.getAttribute('type') ?? 'text').trim().toLowerCase();
    switch (type) {
        case 'button': case 'image': case 'reset': case 'submit': return 'button';
        case 'checkbox': return 'checkbox';
        case 'radio': return 'radio';
        case 'range': return 'slider';
        case 'number': return 'spinbutton';
        case 'search': return e.hasAttribute('list') ? 'combobox' : 'searchbox';
        case 'email': case 'tel': case 'text': case 'url': case '': return e.hasAttribute('list') ? 'combobox' : 'textbox';
        case 'hidden': return 'none';
        default: return 'textbox';
    }
}

/** HTML-AAM implicit role (AriaRoles.GetImplicitRole). */
function implicitRole(e) {
    switch (e.localName) {
        case 'a': case 'area': return e.hasAttribute('href') ? 'link' : 'generic';
        case 'footer': return inSectioningContent(e) ? 'generic' : 'contentinfo';
        case 'header': return inSectioningContent(e) ? 'generic' : 'banner';
        case 'img': return e.getAttribute('alt') === '' && !e.hasAttribute('aria-label') && !e.hasAttribute('aria-labelledby') ? 'presentation' : 'img';
        case 'input': return inputRole(e);
        case 'section': return e.hasAttribute('aria-label') || e.hasAttribute('aria-labelledby') || e.hasAttribute('title') ? 'region' : 'generic';
        case 'select': return e.hasAttribute('multiple') || parseInt(e.getAttribute('size'), 10) > 1 ? 'listbox' : 'combobox';
        case 'th': return (e.getAttribute('scope') || '').toLowerCase() === 'row' ? 'rowheader' : 'columnheader';
        default: return Object.hasOwn(SIMPLE_ROLES, e.localName) ? SIMPLE_ROLES[e.localName] : null;
    }
}

/** The computed role: explicit (unless none/presentation conflicts with focusability or global ARIA) or implicit. */
function roleOf(e) {
    const role = explicitRole(e);
    if ((role === 'none' || role === 'presentation') && (isFocusable(e) || GLOBAL_ARIA.some(a => e.hasAttribute(a)))) return implicitRole(e);
    return role ?? implicitRole(e);
}

// ---- accessible names (AccessibleName) -----------------------------------------------------------------------------

function labelsOf(e, root) {
    const labels = [];
    const id = e.getAttribute('id');
    if (id) labels.push(...Array.from(root.querySelectorAll('label')).filter(l => l.getAttribute('for') === id && inStory(l, root)));
    for (let parent = e.parentElement; parent; parent = parent.parentElement) {
        if (parent.localName === 'label' && !labels.includes(parent) && (!parent.hasAttribute('for') || parent.getAttribute('for') === id)) {
            labels.push(parent);
            break;
        }
    }
    return labels;
}

const firstChild = (e, name) => Array.from(e.children).find(c => c.localName === name);

function labelsText(e, root, visited) {
    return labelsOf(e, root).map(label => contentText(label, root, visited)).filter(t => !blank(t)).join(' ');
}

/** The current value of a control embedded in a label (accname step 2C). */
function controlValue(e) {
    if (e.localName === 'select') {
        const option = e.selectedOptions?.[0] ?? e.options?.[0];
        return option ? textOf(option) : '';
    }
    if (e.localName === 'input' || e.localName === 'textarea') return e.value;
    return e.getAttribute('value') ?? e.getAttribute('aria-valuetext') ?? e.getAttribute('aria-valuenow') ?? e.textContent;
}

function nativeName(e, root, visited) {
    switch (e.localName) {
        case 'input': {
            const type = (e.getAttribute('type') ?? 'text').toLowerCase();
            if (type === 'button' || type === 'submit' || type === 'reset') {
                const value = e.getAttribute('value');
                if (!blank(value)) return value;
                return type === 'submit' ? 'Submit' : type === 'reset' ? 'Reset' : labelsText(e, root, visited);
            }
            if (type === 'image') return e.getAttribute('alt') ?? e.getAttribute('value') ?? '';
            return labelsText(e, root, visited);
        }
        case 'textarea': case 'select': case 'meter': case 'progress': case 'output': case 'button':
            return labelsText(e, root, visited);
        case 'img': case 'area':
            return e.getAttribute('alt') ?? '';
        case 'fieldset': case 'figure': case 'table': {
            const caption = firstChild(e, e.localName === 'fieldset' ? 'legend' : e.localName === 'figure' ? 'figcaption' : 'caption');
            return caption ? computeName(caption, root, visited, false, true) : '';
        }
        case 'svg': {
            const title = firstChild(e, 'title');
            return title ? textOf(title) : '';
        }
        case 'iframe': case 'frame':
            return e.getAttribute('title') ?? '';
        default:
            return '';
    }
}

const NAME_BLOCKS = new Set(['div', 'p', 'li', 'br', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6']);

function contentText(e, root, visited) {
    let text = '';
    for (const child of e.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) text += child.textContent;
        else if (child.nodeType === Node.ELEMENT_NODE && !isExcluded(child)) {
            const block = NAME_BLOCKS.has(child.localName);
            const name = computeName(child, root, visited, false, true);
            text += block ? ' ' + name + ' ' : name;
        }
    }
    return text;
}

function computeName(e, root, visited, inLabelledBy, inContent) {
    if (visited.has(e)) return '';
    visited.add(e);
    if (!inLabelledBy && isExcluded(e)) return '';

    // 2B: aria-labelledby (not followed recursively).
    if (!inLabelledBy) {
        const labelledBy = idRefs(root, e.getAttribute('aria-labelledby'));
        if (labelledBy.length) return labelledBy.map(l => computeName(l, root, visited, true, false)).join(' ');
    }

    // 2C: a control embedded in a label contributes its value.
    const role = roleOf(e);
    if (inContent && ['textbox', 'combobox', 'listbox', 'slider', 'spinbutton'].includes(role)) return controlValue(e);

    // 2D: aria-label.
    const ariaLabel = e.getAttribute('aria-label');
    if (!blank(ariaLabel)) return ariaLabel;

    // 2E: native labelling.
    if (role !== 'presentation' && role !== 'none') {
        const native = nativeName(e, root, visited);
        if (!blank(native)) return native;
    }

    // 2F: name from content.
    if (inLabelledBy || inContent || (role && NAME_FROM_CONTENT.has(role)) || ['label', 'legend', 'caption', 'figcaption', 'summary'].includes(e.localName)) {
        const content = contentText(e, root, visited);
        if (!blank(content)) return content;
    }

    // 2I: tooltip attribute fallbacks.
    return e.getAttribute('title') ?? (e.localName === 'input' || e.localName === 'textarea' ? e.getAttribute('placeholder') ?? '' : '');
}

function accessibleName(e, root) {
    return normalize(computeName(e, root, new Set(), false, false));
}

// ---- queries (DomQueries) ------------------------------------------------------------------------------------------

function labelMatches(e, root, text, exact) {
    const ariaLabel = e.getAttribute('aria-label');
    if (ariaLabel !== null && matches(ariaLabel, text, exact)) return true;
    const labelledBy = idRefs(root, e.getAttribute('aria-labelledby'));
    if (labelledBy.length && matches(labelledBy.map(textOf).join(' '), text, exact)) return true;
    return ['input', 'textarea', 'select', 'button', 'meter', 'output', 'progress'].includes(e.localName)
        && labelsOf(e, root).some(label => matches(textOf(label), text, exact));
}

const isTextCandidate = e => !['script', 'style', 'template', 'head', 'html', 'body'].includes(e.localName);

function textFor(e) {
    return e.localName === 'input' && ['button', 'submit', 'reset'].includes((e.getAttribute('type') || '').toLowerCase())
        ? e.getAttribute('value') ?? ''
        : textOf(e);
}

const innermost = list => list.filter(c => !list.some(o => o !== c && c.contains(o)));

function query(root, candidates, q, within) {
    switch (q.type) {
        case 'css':
            return Array.from(within.querySelectorAll(q.selector)).filter(e => inStory(e, root));
        case 'role': {
            const wanted = q.role.toLowerCase();
            return candidates.filter(e => roleOf(e) === wanted && !isExcluded(e) && (q.name == null || matches(accessibleName(e, root), q.name, q.exact)));
        }
        case 'text':
            return innermost(candidates.filter(e => isTextCandidate(e) && matches(textFor(e), q.value, q.exact)));
        case 'label':
            return candidates.filter(e => labelMatches(e, root, q.value, q.exact));
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

/** Resolves a locator chain to story elements (DomQueries.Resolve). */
function resolve(steps) {
    const root = storyRoot();
    let current = null;
    for (const step of steps) {
        if (step.kind === 'find') {
            if (current === null) current = query(root, storyElements(root), step.query, root);
            else {
                const found = new Set();
                for (const parent of current) for (const e of query(root, Array.from(parent.querySelectorAll('*')).filter(c => inStory(c, root)), step.query, parent)) found.add(e);
                current = [...found];
            }
        } else if (step.kind === 'nth') {
            current = current ?? storyElements(root);
            const i = step.index < 0 ? current.length + step.index : step.index;
            current = i >= 0 && i < current.length ? [current[i]] : [];
        } else if (step.kind === 'hasText') current = (current ?? storyElements(root)).filter(e => matches(textOf(e), step.text, false));
        else if (step.kind === 'hasNotText') current = (current ?? storyElements(root)).filter(e => !matches(textOf(e), step.text, false));
    }
    return current ?? [];
}

const SNIPPET_NOISE = /\s(?:blazor:[^\s=>]+|b-[a-z0-9]{10}|_bl_[0-9a-f-]+)(?:="[^"]*")?/g;

/** DomFacts.Snippet: outer HTML without Blazor bookkeeping attributes, truncated. */
function snippet(e) {
    const html = e.outerHTML.replace(SNIPPET_NOISE, '');
    return html.length > 300 ? html.slice(0, 300) + '…' : html;
}

function describe(e, root) {
    const role = roleOf(e);
    const attrs = {};
    for (const a of e.attributes) if (!a.name.startsWith('_bl_')) attrs[a.name] = a.value;
    let value = null;
    if (['input', 'textarea', 'select'].includes(e.localName)) value = e.value;
    else if (isContentEditable(e)) value = e.textContent;
    return {
        tagName: e.localName, role, accessibleName: accessibleName(e, root), text: textOf(e), value, attributes: attrs,
        isVisible: isVisible(e), isEnabled: !isDisabled(e), isChecked: checkedState(e, role), isFocused: document.activeElement === e,
        isEditable: isEditable(e), html: snippet(e)
    };
}

// ---- events (in-app driver) ----------------------------------------------------------------------------------------

const MODS = m => ({ altKey: !!(m & 1), ctrlKey: !!(m & 2), metaKey: !!(m & 4), shiftKey: !!(m & 8) });
const MODIFIER_BITS = { Alt: 1, Control: 2, Ctrl: 2, Meta: 4, Cmd: 4, ControlOrMeta: 4, Shift: 8 };

/** Keys.Parse: "Shift+Tab" is Tab with Shift; "+" is the plus key. */
function parseChord(value) {
    if (value.length <= 1) return { key: value, mods: 0 };
    const parts = value.split('+');
    const last = parts.pop();
    let mods = 0;
    for (const part of parts) mods |= MODIFIER_BITS[part] || 0;
    return { key: last || '+', mods };
}

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

// Browsers fire `change` when a user-edited control loses focus; scripted edits don't, so emulate it.
function commit(e) {
    if (e && e.__btpDirty) {
        e.__btpDirty = false;
        e.__btpLastCommitted = e.value;
        e.dispatchEvent(new Event('change', { bubbles: true }));
    }
}

let changeOnBlur = false;

function edited(e) {
    if (!changeOnBlur) {
        changeOnBlur = true;
        document.addEventListener('focusout', ev => commit(ev.target), true);
    }
    if (e.__btpValueAtFocus === undefined) e.__btpValueAtFocus = e.__btpLastCommitted ?? '';
    e.__btpDirty = true;
}

function tabOrder() {
    const all = storyElements(storyRoot()).filter(isTabbable);
    const radiosSeen = new Set();
    const filtered = all.filter(e => {
        if (e.localName === 'input' && e.type === 'radio' && e.name) {
            const group = all.filter(r => r.localName === 'input' && r.type === 'radio' && r.name === e.name && r.form === e.form);
            const rep = group.find(r => r.checked) || group[0];
            if (rep !== e || radiosSeen.has(group[0])) return false;
            radiosSeen.add(group[0]);
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
    const index = active ? order.indexOf(active) : -1;
    let next;
    if (index < 0) {
        if (!active) next = backwards ? order.length - 1 : 0;
        else {
            const after = order.findIndex(e => active.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING);
            next = backwards ? (after < 0 ? order.length - 1 : after - 1) : after;
        }
    } else next = backwards ? index - 1 : index + 1;
    if (next < 0 || next >= order.length) { if (active) active.blur(); return; }
    order[next].focus();
}

// Enter and Space click buttons, button-like inputs and the summary of a details element. Enter also follows links;
// Space also toggles checkboxes and checks radios. Neither does anything else: Enter on a checkbox doesn't toggle it.
const isKeyboardClickable = e => e.localName === 'button' || isDetailsSummary(e)
    || (e.localName === 'input' && ['button', 'submit', 'reset', 'image', 'file', 'color'].includes(e.type));
const isCheckable = e => e.localName === 'input' && (e.type === 'checkbox' || e.type === 'radio');

/** Inputs whose form submits on Enter when it has no submit button and only one of them. */
const blocksImplicitSubmission = e => e.localName === 'input'
    && !['hidden', 'checkbox', 'radio', 'range', 'color', 'file', 'button', 'submit', 'reset', 'image'].includes(e.type);

/**
 * Implicit submission (Enter in any other input): the edit is committed (change) first; then the owner form's default
 * button (its first submit button) is clicked unless it is disabled. A form without one submits only when the field is
 * its single text-like field.
 */
function submitImplicitly(field) {
    commit(field);
    const form = field.form;
    if (!form) return;
    const defaultButton = Array.from(form.elements).find(c => (c.localName === 'button' && c.type === 'submit') || (c.localName === 'input' && (c.type === 'submit' || c.type === 'image')));
    if (defaultButton) {
        if (!defaultButton.matches(':disabled')) defaultButton.click();
        return;
    }
    if (blocksImplicitSubmission(field) && Array.from(form.elements).filter(blocksImplicitSubmission).length === 1) form.requestSubmit();
}

function insertText(e, ch) {
    edited(e);
    if (e.isContentEditable) e.textContent += ch; else setValue(e, (e.value || '') + ch);
    e.dispatchEvent(new InputEvent('input', { bubbles: true, data: ch, inputType: 'insertText' }));
}

export function pressKey(k, mods) {
    const pressed = document.activeElement || document.body;
    const printable = (k.length === 1 || k === 'Space') && !(mods & (1 | 2 | 4));
    const notPrevented = key(pressed, 'keydown', k, mods);

    // A keydown handler that moves focus (roving tabindex, dialogs) redirects the rest of the key press.
    const target = document.activeElement || document.body;
    if (printable) key(target, 'keypress', k, mods);
    if (notPrevented) {
        if (k === 'Tab') moveFocus(!!(mods & 8));
        else if (k === 'Enter') {
            if (isKeyboardClickable(target) || isLink(target)) target.click();
            else if (target.localName === 'input') submitImplicitly(target);
        } else if (isEditable(target) && printable) insertText(target, k === 'Space' ? ' ' : k);
        else if (isEditable(target) && k === 'Backspace' && (target.value || '').length > 0) {
            edited(target);
            setValue(target, target.value.slice(0, -1));
            target.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward' }));
        }
    }
    key(document.activeElement || document.body, 'keyup', k, mods);

    // Space clicks on keyup, if the pressed element kept focus. Never links.
    const space = k === 'Space' || k === ' ';
    if (notPrevented && space && document.activeElement === pressed && (isKeyboardClickable(pressed) || isCheckable(pressed))) pressed.click();
}

function click(e, kind, mods, button) {
    e.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    mouse(e, 'pointerover', {}); mouse(e, 'mouseover', {});
    const clicks = kind === 'dblclick' ? 2 : 1;
    for (let i = 1; i <= clicks; i++) {
        mouse(e, 'pointerdown', { button, buttons: 1, ...MODS(mods) });
        mouse(e, 'mousedown', { button, buttons: 1, detail: i, ...MODS(mods) });
        let target = e;
        while (target && !isFocusable(target)) target = target.parentElement;
        if (target) target.focus(); else if (document.activeElement) { commit(document.activeElement); document.activeElement.blur(); }
        mouse(e, 'pointerup', { button, ...MODS(mods) });
        mouse(e, 'mouseup', { button, detail: i, ...MODS(mods) });
        if (button === 2) mouse(e, 'contextmenu', { button, detail: i, ...MODS(mods) });
        else if (mods === 0 && button === 0) e.click();
        else mouse(e, 'click', { button, detail: i, ...MODS(mods) });
    }
    if (clicks === 2) mouse(e, 'dblclick', { button, detail: 2, ...MODS(mods) });
}

// ---- API used by the C# drivers ------------------------------------------------------------------------------------

export function queryAll(steps) {
    const root = storyRoot();
    return resolve(steps).map(e => describe(e, root));
}

/** The focused story element, or null when focus is on the body or outside the story. */
export function focused() {
    const root = storyRoot();
    const e = document.activeElement;
    return !e || e === document.body || e === document.documentElement || !inStory(e, root) ? null : describe(e, root);
}

/** Checked state of an element (null when it is not checkable). */
export function checkedOf(e) {
    return checkedState(e, roleOf(e));
}

/** What CheckAsync/UncheckAsync must do: 'click' to change the state, null when it already matches, or the problem. */
export function checkPlan(e, want) {
    const state = checkedOf(e);
    if (state === null) return 'element is not a checkbox, radio or switch';
    if (state === want) return null;
    if (!want && e.localName === 'input' && e.type === 'radio') return 'cannot uncheck a radio button';
    return 'click';
}

const NEEDS_ENABLED = ['click', 'dblclick', 'fill', 'clear', 'type', 'check', 'uncheck', 'select'];
const FOCUS_ONLY = ['focus', 'blur', 'scroll'];
const POINTER_ACTIONS = ['click', 'dblclick', 'hover', 'pointerdown', 'pointerup'];
let lastTarget = null;

/** Resolves the single element an action targets, or returns a problem to retry on (strictness, visibility, state). */
export function actionTarget(steps, action) {
    const found = resolve(steps);
    if (found.length !== 1) return found.length === 0 ? 'no element matches' : `strict mode violation: ${found.length} elements match`;
    const e = found[0];
    if (!action.force) {
        // Pointer and typing actions need what a user sees (a box); focus, blur and scrolling only need a rendered element.
        if (FOCUS_ONLY.includes(action.kind) ? isHidden(e) : !isVisible(e)) return 'element is not visible';
        if (!FOCUS_ONLY.includes(action.kind) && e.closest('[inert]')) return 'element is inert';
        if (NEEDS_ENABLED.includes(action.kind) && isDisabled(e)) return 'element is disabled';
    }
    if ((action.kind === 'fill' || action.kind === 'clear') && !isEditable(e) && !(e.localName === 'input' && ['color', 'range'].includes(e.type))) return 'element is not editable';
    lastTarget = e;
    return e;
}

/** Where a pointer aimed at an element lands: hit testing skips inert content (reached only by forced actions). */
function hitTarget(e) {
    let hit = e;
    while (hit && hit.closest('[inert]')) hit = hit.parentElement;
    return hit && (hit === e || inStory(hit, storyRoot())) ? hit : null;
}

/** Checked state of the last action's target, re-resolving the locator when a re-render replaced it. */
export function targetChecked(steps) {
    if (lastTarget && lastTarget.isConnected) return checkedOf(lastTarget);
    const found = resolve(steps);
    return found.length === 1 ? checkedOf(found[0]) : null;
}

/** Performs an action; returns null on success or a problem description to retry on. */
export function perform(steps, action) {
    let e = actionTarget(steps, action);
    if (typeof e === 'string') return e;
    if (POINTER_ACTIONS.includes(action.kind)) {
        e = hitTarget(e);
        if (!e) return null;
    }
    const mods = action.modifiers || 0, button = action.button || 0;
    switch (action.kind) {
        case 'click': case 'dblclick':
            click(e, action.kind, mods, button);
            return null;
        case 'hover':
            for (const t of ['pointerover', 'pointerenter', 'mouseover', 'mouseenter', 'pointermove', 'mousemove']) mouse(e, t, {});
            return null;
        case 'focus': e.focus(); return null;
        case 'blur': commit(e); e.blur(); return null;
        case 'fill': case 'clear': {
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
            const chord = parseChord(action.text);
            pressKey(chord.key, chord.mods);
            return null;
        }
        case 'check': case 'uncheck': {
            // Click once; the C# driver then waits for the state, which Server circuits and async handlers update later.
            const plan = checkPlan(e, action.kind === 'check');
            if (plan !== 'click') return plan;
            click(e, 'click', 0, 0);
            return null;
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
            const target = actionTarget(action.target, { kind: 'hover' });
            if (typeof target === 'string') return 'drop target: ' + target;
            const data = new DataTransfer();
            const drag = (el, type) => el.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: data }));
            drag(e, 'dragstart'); drag(target, 'dragenter'); drag(target, 'dragover'); drag(target, 'drop'); drag(e, 'dragend');
            return null;
        }
        case 'scroll': e.scrollIntoView({ block: 'center' }); return null;
        default: return 'unsupported action ' + action.kind;
    }
}

export function keyboard(kind, value) {
    const { key: k, mods } = parseChord(value);
    const target = document.activeElement || document.body;
    if (kind === 'press') pressKey(k, mods);
    else if (kind === 'type') for (const ch of value) pressKey(ch, 0);
    else if (kind === 'down') key(target, 'keydown', k, mods);
    else if (kind === 'up') key(target, 'keyup', k, mods);
}

export function settle() {
    return new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(true))));
}

export function markup() {
    const root = document.querySelector(ROOT);
    return root ? root.innerHTML : '';
}

export function publishResult(storyId, result) {
    const root = document.querySelector(ROOT);
    if (root) root.setAttribute('data-btp-play-status', result.status);
    window.__btpPlayResult = result;
    if (window.parent && window.parent !== window) window.parent.postMessage({ source: 'btp', type: 'play-result', storyId, ...result }, '*');
}

// JSON entry points (the .NET side passes and receives strings, which keeps interop trimming-safe).
export function queryJson(stepsJson) { return JSON.stringify(queryAll(JSON.parse(stepsJson))); }
export function performJson(stepsJson, actionJson) { return perform(JSON.parse(stepsJson), JSON.parse(actionJson)); }
export function actionTargetJson(stepsJson, actionJson) { return actionTarget(JSON.parse(stepsJson), JSON.parse(actionJson)); }
export function targetCheckedJson(stepsJson) { return JSON.stringify(targetChecked(JSON.parse(stepsJson))); }
export function focusedJson() { const f = focused(); return f ? JSON.stringify(f) : null; }
export function publishResultJson(storyId, resultJson) { publishResult(storyId, JSON.parse(resultJson)); }
