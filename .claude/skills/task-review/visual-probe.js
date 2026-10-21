// Measures the page in the task's Browser pane for /task-review's visual pass. Pass this file's text to
// kangentic_browser_eval once per page load; it returns JSON and leaves `visualProbe()` behind for the other widths.
// Raw measurements only: the limits they are judged against live in docs/design/design-system.md section 2, and the
// design-review agent applies them.
(globalThis.visualProbe = () => {
  const root = document.documentElement;
  const visible = (el) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const describe = (el) => {
    const id = el.id ? `#${el.id}` : '';
    const slot = el.dataset.slot ? `[data-slot=${el.dataset.slot}]` : '';
    const text = (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 60);
    return `${el.tagName.toLowerCase()}${id}${slot} "${text}"`;
  };
  // An ancestor's overflow clips an element only from the element's containing block up: an absolute element
  // (an sr-only label in a table cell) escapes a scroll container that sits below its positioned ancestor and
  // widens the page. A fixed element (a closed sheet, a toast) never widens it.
  const clipped = (el) => {
    const position = getComputedStyle(el).position;
    if (position === 'fixed') return true;
    let containingBlock = position === 'absolute' ? el.offsetParent : null;
    for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
      if (containingBlock && p !== containingBlock) continue;
      containingBlock = null;
      if (/(auto|scroll|hidden|clip)/.test(getComputedStyle(p).overflowX)) return true;
    }
    return false;
  };
  // A control wraps when one of its text nodes breaks onto a second line. Stacked blocks (a name over an
  // organization) are separate text nodes and do not count.
  const wraps = (el) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (!n.textContent.trim()) continue;
      const range = document.createRange();
      range.selectNodeContents(n);
      const rects = [...range.getClientRects()].filter((r) => r.width > 0);
      if (rects.some((r) => r.top >= rects[0].bottom - 1)) return true;
    }
    return false;
  };
  const all = [...document.body.querySelectorAll('*')].filter(visible);
  // sr-only controls (1px boxes) are not targets anyone taps.
  const targets = all.filter((el) =>
    el.matches('a[href], button, input:not([type=hidden]), select, textarea, summary, [role=button], [role=tab], [role=checkbox], [role=switch], [role=menuitem], [role=option]') &&
    el.clientWidth > 1,
  );
  // A wrapping label is an input's hit area, so its size is the target's size.
  const hitBox = (el) => (el.matches('input') && el.closest('label') ? el.closest('label') : el).getBoundingClientRect();
  const main = document.querySelector('main');
  const mainStyle = main && getComputedStyle(main);

  const fontSizes = {};
  for (const el of all) {
    const ownText = [...el.childNodes].some((n) => n.nodeType === Node.TEXT_NODE && n.textContent.trim());
    if (!ownText) continue;
    const size = getComputedStyle(el).fontSize;
    (fontSizes[size] ??= []).length < 3 && fontSizes[size].push(describe(el));
  }

  return JSON.stringify({
    url: location.pathname + location.search,
    viewport: { width: innerWidth, height: innerHeight, pointerCoarse: matchMedia('(pointer: coarse)').matches },
    page: { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, h1: document.querySelector('h1')?.textContent.trim() ?? null },
    main: main && { width: Math.round(main.getBoundingClientRect().width), maxWidth: mainStyle.maxWidth, count: document.querySelectorAll('main').length },
    pastRightEdge: all
      .filter((el) => el.getBoundingClientRect().right > root.clientWidth + 1 && !clipped(el))
      .slice(0, 10)
      .map((el) => ({ el: describe(el), class: (el.getAttribute('class') ?? '').slice(0, 80), position: getComputedStyle(el).position, right: Math.round(el.getBoundingClientRect().right) })),
    smallestTargets: targets
      .map((el) => ({ el: describe(el), w: Math.round(hitBox(el).width), h: Math.round(hitBox(el).height) }))
      .sort((a, b) => Math.min(a.w, a.h) - Math.min(b.w, b.h))
      .slice(0, 12),
    wrappedControls: targets
      .filter((el) => !el.matches('input, select, textarea') && wraps(el))
      .slice(0, 10)
      .map(describe),
    truncated: all
      // sr-only labels are 1px boxes that always overflow; they are not visible truncation.
      .filter((el) => el.clientWidth > 1 && getComputedStyle(el).textOverflow === 'ellipsis' && el.scrollWidth > el.clientWidth)
      .slice(0, 10)
      .map((el) => ({ el: describe(el), title: el.getAttribute('title'), inTooltipTrigger: !!el.closest('[data-slot=tooltip-trigger]') })),
    fontSizes,
    nestedPanels: [...document.querySelectorAll('[data-slot=panel] [data-slot=panel]')].map(describe),
    shadows: all
      .filter((el) => {
        const s = getComputedStyle(el).boxShadow;
        return s !== 'none' && !/ 0px 0px 0px /.test(s);
      })
      .slice(0, 10)
      .map((el) => ({ el: describe(el), boxShadow: getComputedStyle(el).boxShadow })),
  });
})()
