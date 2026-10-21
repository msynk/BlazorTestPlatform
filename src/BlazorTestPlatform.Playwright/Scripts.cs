namespace BlazorTestPlatform.Playwright;

/// <summary>JavaScript snippets evaluated in the page.</summary>
internal static class Scripts
{
    /// <summary>
    /// Describes elements in the same shape as <c>ElementInfo</c>. Role and accessible name follow the same simplified
    /// accname rules as the in-memory engine so assertions agree across drivers.
    /// </summary>
    public const string DescribeFunction = """
        (el) => {
          const norm = s => (s || '').replace(/[\s​]+/g, ' ').trim();
          const implicitRole = e => {
            const t = e.localName, type = (e.getAttribute('type') || 'text').toLowerCase();
            switch (t) {
              case 'a': case 'area': return e.hasAttribute('href') ? 'link' : 'generic';
              case 'button': return 'button';
              case 'input':
                if (['button','image','reset','submit'].includes(type)) return 'button';
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
              case 'div': case 'span': return 'generic';
              default: return null;
            }
          };
          const role = (() => { const r = (el.getAttribute('role') || '').trim().split(/\s+/)[0]; return r || implicitRole(el); })();
          const text = e => norm(e.innerText !== undefined && e.checkVisibility && e.checkVisibility() ? e.innerText : e.textContent);
          const name = (() => {
            const by = el.getAttribute('aria-labelledby');
            if (by) { const parts = by.split(/\s+/).map(id => document.getElementById(id)).filter(Boolean).map(n => norm(n.textContent)); if (parts.length) return norm(parts.join(' ')); }
            const label = el.getAttribute('aria-label'); if (label && label.trim()) return norm(label);
            if (el.labels && el.labels.length) return norm(Array.from(el.labels).map(l => l.textContent).join(' '));
            if (el.localName === 'img' || el.localName === 'area') return norm(el.getAttribute('alt'));
            if (el.localName === 'input' && ['button','submit','reset'].includes((el.type||'').toLowerCase())) return norm(el.value || (el.type === 'submit' ? 'Submit' : el.type === 'reset' ? 'Reset' : ''));
            const fromContent = ['button','cell','checkbox','columnheader','gridcell','heading','link','menuitem','menuitemcheckbox','menuitemradio','option','radio','row','rowheader','switch','tab','tooltip','treeitem'];
            if (role && fromContent.includes(role)) { const c = norm(el.textContent); if (c) return c; }
            return norm(el.getAttribute('title') || el.getAttribute('placeholder') || '');
          })();
          const attrs = {};
          for (const a of el.attributes) { if (!a.name.startsWith('_bl_')) attrs[a.name] = a.value; }
          const style = getComputedStyle(el);
          const rect = el.getBoundingClientRect();
          const visible = (el.checkVisibility ? el.checkVisibility({ checkOpacity: false, checkVisibilityCSS: true }) : style.display !== 'none' && style.visibility !== 'hidden') && (rect.width > 0 || rect.height > 0);
          const disabled = (el.matches && el.matches(':disabled')) || el.getAttribute('aria-disabled') === 'true';
          let checked = null;
          if (el.localName === 'input' && (el.type === 'checkbox' || el.type === 'radio')) checked = !!el.checked;
          else if (['checkbox','radio','switch','menuitemcheckbox','menuitemradio'].includes(role)) checked = el.getAttribute('aria-checked') === 'true';
          else if (role === 'option' || role === 'treeitem') { const s = el.getAttribute('aria-selected'); checked = s === null ? null : s === 'true'; }
          else if (role === 'button' && el.hasAttribute('aria-pressed')) checked = el.getAttribute('aria-pressed') === 'true';
          const editable = !disabled && !el.readOnly && (el.isContentEditable || el.localName === 'textarea' ||
            (el.localName === 'input' && ['text','search','email','url','tel','password','number','date','datetime-local','month','time','week'].includes((el.getAttribute('type') || 'text').toLowerCase())));
          let value = null;
          if ('value' in el && ['input','textarea','select'].includes(el.localName)) value = el.value;
          else if (el.isContentEditable) value = el.textContent;
          const html = el.outerHTML.replace(/\s_bl_[\w-]+(="[^"]*")?/g, '');
          return {
            tagName: el.localName, role, accessibleName: name, text: norm(el.textContent), value, attributes: attrs,
            isVisible: visible, isEnabled: !disabled, isChecked: checked, isFocused: document.activeElement === el,
            isEditable: editable, html: html.length > 300 ? html.slice(0, 300) + '…' : html
          };
        }
        """;

    /// <summary>Describes every element matched by a locator.</summary>
    public const string DescribeAll = "(elements) => elements.map(" + DescribeFunction + ")";

    /// <summary>Describes the focused element (null for body).</summary>
    public const string DescribeFocused = "() => { const el = document.activeElement; if (!el || el === document.body || el === document.documentElement) return null; return (" + DescribeFunction + ")(el); }";

    /// <summary>CSS injected before snapshots: no animations, transitions or carets.</summary>
    public const string StabilizeCss = "*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;transition-duration:0s!important;transition-delay:0s!important;caret-color:transparent!important;scroll-behavior:auto!important}";

    /// <summary>Waits for fonts and images.</summary>
    public const string WaitForAssets = """
        async () => {
          if (document.fonts && document.fonts.ready) { await document.fonts.ready; }
          const images = Array.from(document.images).filter(i => !i.complete);
          await Promise.all(images.map(i => new Promise(r => { i.addEventListener('load', r, { once: true }); i.addEventListener('error', r, { once: true }); })));
          await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          return true;
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
