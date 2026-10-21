import { vi } from 'vitest';

/**
 * Answers `matchMedia` width queries as a window `width` pixels wide would, so the sidebar and the shell's MediaQuery
 * see a phone, a tablet or a laptop. jsdom has no media queries of its own. The returned `resize` changes the width and
 * tells every query's listeners, as a browser does when a window is resized. Restore with `vi.unstubAllGlobals()`.
 */
export function stubViewport(width: number): { resize(width: number): void } {
  let current = width;
  const queries = new Set<EventTarget>();
  vi.stubGlobal('matchMedia', (query: string) => {
    const list = Object.defineProperties(new EventTarget(), {
      matches: { get: () => matchesWidth(query, current) },
      media: { value: query },
      onchange: { value: null, writable: true },
      addListener: { value() {} },
      removeListener: { value() {} },
    });
    queries.add(list);
    return list;
  });
  return {
    resize(width: number) {
      current = width;
      for (const list of queries) list.dispatchEvent(new Event('change'));
    },
  };
}

function matchesWidth(query: string, width: number): boolean {
  const conditions = [...query.matchAll(/\((min|max)-width:\s*(\d+)px\)/g)];
  return conditions.length > 0 && conditions.every(([, kind, value]) => (kind === 'min' ? width >= Number(value) : width <= Number(value)));
}
